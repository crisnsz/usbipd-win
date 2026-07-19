// SPDX-FileCopyrightText: 2026 crisnsz
//
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Usbipd;

sealed partial class BindPipeServer : BackgroundService
{
    readonly ILogger Logger;
    readonly SecurityIdentifier? UsbipdUsersGroupSid;
    readonly SemaphoreSlim DispatchLock = new(1, 1);

    public BindPipeServer(ILogger<BindPipeServer> logger)
    {
        Logger = logger;
        UsbipdUsersGroupSid = TryGetLocalGroupSid(BindPipeProtocol.PipeGroupName);
        if (UsbipdUsersGroupSid is null)
        {
            LogGroupNotResolved(BindPipeProtocol.PipeGroupName);
        }
    }

    public override void Dispose()
    {
        DispatchLock.Dispose();
        base.Dispose();
    }

    PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        if (UsbipdUsersGroupSid is not null)
        {
            security.AddAccessRule(new PipeAccessRule(
                UsbipdUsersGroupSid,
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                AccessControlType.Allow));
        }
        return security;
    }

    bool IsCallerAuthorized(NamedPipeServerStream pipe)
    {
        try
        {
            var authorized = false;
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                authorized =
                    principal.IsInRole(WindowsBuiltInRole.Administrator)
                    || (UsbipdUsersGroupSid is not null && principal.IsInRole(UsbipdUsersGroupSid));
            });
            return authorized;
        }
        catch (IOException ex)
        {
            LogAuthCheckFailed(ex);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogAuthCheckFailed(ex);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            LogAuthCheckFailed(ex);
            return false;
        }
    }

    static SecurityIdentifier? TryGetLocalGroupSid(string groupName)
    {
        try
        {
            return (SecurityIdentifier)new NTAccount(Environment.MachineName, groupName)
                .Translate(typeof(SecurityIdentifier));
        }
        catch (IdentityNotMappedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Warning,
        Message = "Local group '{GroupName}' could not be resolved; only Administrators will be able to use the bind pipe.")]
    partial void LogGroupNotResolved(string groupName);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Authorization check on bind pipe failed.")]
    partial void LogAuthCheckFailed(Exception ex);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Bind pipe connection error ({ExceptionType}): {Message}")]
    partial void LogConnectionError(string exceptionType, string message);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Rejected bind pipe connection from unauthorized caller.")]
    partial void LogUnauthorizedConnection();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
#pragma warning disable CA2000 // Ownership is transferred to HandleAndDisposeAsync (or disposed inline below on failure).
                pipe = NamedPipeServerStreamAcl.Create(
                    BindPipeProtocol.PipeName,
                    PipeDirection.InOut,
                    BindPipeProtocol.MaxServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    CreatePipeSecurity());
#pragma warning restore CA2000
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogConnectionError(ex.GetType().Name, ex.Message);
                try
                {
                    await Task.Delay(1_000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync();
                return;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogConnectionError(ex.GetType().Name, ex.Message);
                await pipe.DisposeAsync();
                continue;
            }

            _ = HandleAndDisposeAsync(pipe, stoppingToken);
        }
    }

    async Task HandleAndDisposeAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        try
        {
            await HandleConnectionAsync(pipe, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            LogConnectionError(ex.GetType().Name, ex.Message);
        }
        finally
        {
            try
            {
                if (pipe.IsConnected)
                {
                    pipe.Disconnect();
                }
            }
#pragma warning disable CA1031
            catch
#pragma warning restore CA1031
            {
            }
            await pipe.DisposeAsync();
        }
    }

    async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        if (!IsCallerAuthorized(pipe))
        {
            LogUnauthorizedConnection();
            await WriteResponseAsync(
                pipe,
                ExitCode.AccessDenied,
                rebootRequired: false,
                [new BindPipeMessage(
                    BindPipeMessageLevel.Error,
                    $"Access denied; add your account to the '{BindPipeProtocol.PipeGroupName}' local group or run as administrator.")],
                stoppingToken);
            return;
        }

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        readCts.CancelAfter(BindPipeProtocol.RequestReadTimeoutMs);
        var readToken = readCts.Token;

        var command = (BindPipeCommand)await ReadByteAsync(pipe, readToken);

        string? instanceId = null;
        var force = false;
        Guid guid = default;

        switch (command)
        {
            case BindPipeCommand.Bind:
                instanceId = await ReadStringAsync(pipe, BindPipeProtocol.MaxStringByteLength, readToken);
                force = await ReadByteAsync(pipe, readToken) != 0;
                break;
            case BindPipeCommand.Unbind:
                {
                    var guidBytes = await ReadExactAsync(pipe, 16, readToken);
                    guid = new Guid(guidBytes);
                    break;
                }
            case BindPipeCommand.UnbindAll:
                break;
            default:
                await WriteResponseAsync(
                    pipe,
                    ExitCode.Failure,
                    rebootRequired: false,
                    [new BindPipeMessage(BindPipeMessageLevel.Error, "Unknown bind command.")],
                    stoppingToken);
                return;
        }

        var messages = new List<BindPipeMessage>();
        bool rebootRequired;
        ExitCode exitCode;

        await DispatchLock.WaitAsync(stoppingToken);
        try
        {
            switch (command)
            {
                case BindPipeCommand.Bind:
                    exitCode = BindService.Bind(instanceId!, force, out rebootRequired, messages);
                    break;
                case BindPipeCommand.Unbind:
                    exitCode = BindService.Unbind(guid, out rebootRequired, messages);
                    break;
                case BindPipeCommand.UnbindAll:
                    exitCode = BindService.UnbindAll(out rebootRequired, messages);
                    break;
                default:
                    exitCode = ExitCode.Failure;
                    rebootRequired = false;
                    break;
            }
        }
        finally
        {
            _ = DispatchLock.Release();
        }

        await WriteResponseAsync(pipe, exitCode, rebootRequired, messages, stoppingToken);
    }

    static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        if (count == 0)
        {
            return Array.Empty<byte>();
        }
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Pipe client closed the connection before sending the full request.");
            }
            offset += read;
        }
        return buffer;
    }

    static async Task<byte> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = await ReadExactAsync(stream, 1, cancellationToken);
        return buffer[0];
    }

    static async Task<string> ReadStringAsync(Stream stream, int maxByteLength, CancellationToken cancellationToken)
    {
        var byteLength = 0;
        var shift = 0;
        while (true)
        {
            if (shift >= 5 * 7)
            {
                throw new InvalidDataException("String length prefix is malformed.");
            }
            var b = await ReadByteAsync(stream, cancellationToken);
            byteLength |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }
            shift += 7;
        }
        if (byteLength < 0 || byteLength > maxByteLength)
        {
            throw new InvalidDataException($"String length {byteLength} exceeds maximum {maxByteLength}.");
        }
        var bytes = await ReadExactAsync(stream, byteLength, cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

    static async Task WriteResponseAsync(
        Stream stream, ExitCode exitCode, bool rebootRequired,
        List<BindPipeMessage> messages, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((byte)exitCode);
            bw.Write(rebootRequired);
            var count = Math.Min(messages.Count, byte.MaxValue);
            bw.Write((byte)count);
            for (var i = 0; i < count; i++)
            {
                var msg = messages[i];
                bw.Write((byte)msg.Level);
                bw.Write(msg.Text);
            }
        }
        var buffer = ms.ToArray();
        await stream.WriteAsync(buffer, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
