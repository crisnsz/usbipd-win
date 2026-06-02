using System.IO.Pipes;
using System.Security.Principal;

namespace Usbipd;

static class BindPipeClient
{
    static async Task<ExitCode> SendAsync(
        Action<BinaryWriter> writeRequest, IConsole console, CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(
            ".", BindPipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Impersonation);

        try
        {
            await pipe.ConnectAsync(BindPipeProtocol.ConnectTimeoutMs, cancellationToken);
        }
        catch (TimeoutException)
        {
            console.ReportError("Cannot connect to the service; ensure the usbipd service is running.");
            return ExitCode.Failure;
        }
        catch (UnauthorizedAccessException)
        {
            console.ReportError($"Access denied; add your account to the 'usbipd_users' local group or run as administrator.");
            return ExitCode.AccessDenied;
        }

        using var writer = new BinaryWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
        using var reader = new BinaryReader(pipe, System.Text.Encoding.UTF8, leaveOpen: true);

        writeRequest(writer);
        writer.Flush();
        await pipe.FlushAsync(cancellationToken);

        var exitCode = (ExitCode)reader.ReadByte();
        var rebootRequired = reader.ReadBoolean();
        var messageCount = reader.ReadByte();

        for (var i = 0; i < messageCount; i++)
        {
            var level = (BindPipeMessageLevel)reader.ReadByte();
            var text = reader.ReadString();
            switch (level)
            {
                case BindPipeMessageLevel.Info:
                    console.ReportInfo(text);
                    break;
                case BindPipeMessageLevel.Warning:
                    console.ReportWarning(text);
                    break;
                case BindPipeMessageLevel.Error:
                    console.ReportError(text);
                    break;
            }
        }

        if (rebootRequired)
        {
            console.ReportRebootRequired();
        }

        return exitCode;
    }

    public static Task<ExitCode> BindAsync(
        string instanceId, string description, bool force,
        IConsole console, CancellationToken cancellationToken)
    {
        return SendAsync(w =>
        {
            w.Write((byte)BindPipeCommand.Bind);
            w.Write(instanceId);
            w.Write(description);
            w.Write(force);
        }, console, cancellationToken);
    }

    public static Task<ExitCode> UnbindAsync(
        Guid guid,
        IConsole console, CancellationToken cancellationToken)
    {
        return SendAsync(w =>
        {
            w.Write((byte)BindPipeCommand.Unbind);
            w.Write(guid.ToByteArray());
        }, console, cancellationToken);
    }

    public static Task<ExitCode> UnbindAllAsync(
        IConsole console, CancellationToken cancellationToken)
    {
        return SendAsync(w =>
        {
            w.Write((byte)BindPipeCommand.UnbindAll);
        }, console, cancellationToken);
    }
}
