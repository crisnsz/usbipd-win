// SPDX-FileCopyrightText: 2026 crisnsz
//
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Pipes;
using System.Security.Principal;

namespace Usbipd;

static class BindPipeClient
{
    static async Task<(ExitCode exitCode, bool rebootRequired)> SendAsync(
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
            return (ExitCode.Failure, false);
        }
        catch (UnauthorizedAccessException)
        {
            console.ReportError($"Access denied; add your account to the 'usbipd-users' local group or run as administrator.");
            return (ExitCode.AccessDenied, false);
        }

        using var writer = new BinaryWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
        using var reader = new BinaryReader(pipe, System.Text.Encoding.UTF8, leaveOpen: true);

        writeRequest(writer);
        writer.Flush();
        await pipe.FlushAsync(cancellationToken);

        var exitCode = (ExitCode)reader.ReadByte();
        var rebootRequired = reader.ReadBoolean();
        var messageCount = reader.ReadByte();

        var messages = new List<BindPipeMessage>(messageCount);
        for (var i = 0; i < messageCount; i++)
        {
            var level = (BindPipeMessageLevel)reader.ReadByte();
            var text = reader.ReadString();
            messages.Add(new(level, text));
        }
        messages.Relay(console);

        return (exitCode, rebootRequired);
    }

    public static Task<(ExitCode exitCode, bool rebootRequired)> BindAsync(
        string instanceId, bool force,
        IConsole console, CancellationToken cancellationToken)
    {
        return SendAsync(w =>
        {
            w.Write((byte)BindPipeCommand.Bind);
            w.Write(instanceId);
            w.Write(force);
        }, console, cancellationToken);
    }

    public static Task<(ExitCode exitCode, bool rebootRequired)> UnbindAsync(
        Guid guid,
        IConsole console, CancellationToken cancellationToken)
    {
        return SendAsync(w =>
        {
            w.Write((byte)BindPipeCommand.Unbind);
            w.Write(guid.ToByteArray());
        }, console, cancellationToken);
    }

    public static Task<(ExitCode exitCode, bool rebootRequired)> UnbindAllAsync(
        IConsole console, CancellationToken cancellationToken)
    {
        return SendAsync(w =>
        {
            w.Write((byte)BindPipeCommand.UnbindAll);
        }, console, cancellationToken);
    }
}
