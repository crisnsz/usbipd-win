using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Usbipd;

sealed partial class BindPipeServer : BackgroundService
{
    readonly ILogger Logger;

    public BindPipeServer(ILogger<BindPipeServer> logger)
    {
        Logger = logger;
    }

    static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }

    bool IsCallerAuthorized(NamedPipeServerStream pipe)
    {
        try
        {
            LogPipeConnected(pipe.IsConnected);
            var authorized = false;

            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                var localUsersGroupSid = TryGetLocalGroupSid(BindPipeProtocol.PipeGroupName);
                var isInUsbipdUsers =
                    principal.IsInRole(BindPipeProtocol.PipeGroupName) ||
                    (localUsersGroupSid is not null && identity.Groups?.Contains(localUsersGroupSid) == true);

                authorized =
                    principal.IsInRole(WindowsBuiltInRole.Administrator) ||
                    isInUsbipdUsers;
            });

            return authorized;
        }
        catch (IOException ex)
        {
            LogErrorIsCallerAuthorized(ex);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogErrorIsCallerAuthorized(ex);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            LogErrorIsCallerAuthorized(ex);
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

    [LoggerMessage(
    EventId = 1000,
    Level = LogLevel.Information,
    Message = "Connected: {IsConnected}")]
    private partial void LogPipeConnected(bool isConnected);


    [LoggerMessage(
    EventId = 1001,
    Level = LogLevel.Error,
    Message = "RunAsClient failed")]
    private partial void LogErrorIsCallerAuthorized(Exception ex);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pipeSecurity = CreatePipeSecurity();

        while (!stoppingToken.IsCancellationRequested)
        {
            using var pipe = NamedPipeServerStreamAcl.Create(
                BindPipeProtocol.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity);

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await HandleConnectionAsync(pipe, stoppingToken);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.Debug($"Bind pipe connection error: {ex.Message}");
                }
            }

            if (pipe.IsConnected)
            {
                pipe.Disconnect();
            }
        }
    }

    async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var writer = new BinaryWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
        using var reader = new BinaryReader(pipe, System.Text.Encoding.UTF8, leaveOpen: true);

        var command = (BindPipeCommand)reader.ReadByte();
        var isCallerAuthorized = IsCallerAuthorized(pipe);
        if (!isCallerAuthorized)
        {
            DrainRequestPayload(reader, command);

            writer.Write((byte)ExitCode.AccessDenied);
            writer.Write(false); 
            writer.Write((byte)1); 
            writer.Write((byte)BindPipeMessageLevel.Error);
            writer.Write($"Access denied; add your account to the '{BindPipeProtocol.PipeGroupName}' local group or run as administrator.");
            writer.Flush();
            await pipe.FlushAsync(cancellationToken);
            return;
        }

        var messages = new List<BindPipeMessage>();
        bool rebootRequired;
        ExitCode exitCode;

        switch (command)
        {
            case BindPipeCommand.Bind:
                {
                    var instanceId = reader.ReadString();
                    var description = reader.ReadString();
                    var force = reader.ReadBoolean();
                    exitCode = BindService.Bind(instanceId, description, force, out rebootRequired, messages);
                    break;
                }
            case BindPipeCommand.Unbind:
                {
                    var guidBytes = reader.ReadBytes(16);
                    var guid = new Guid(guidBytes);
                    exitCode = BindService.Unbind(guid, out rebootRequired, messages);
                    break;
                }
            case BindPipeCommand.UnbindAll:
                exitCode = BindService.UnbindAll(out rebootRequired, messages);
                break;
            default:
                exitCode = ExitCode.Failure;
                rebootRequired = false;
                messages.Add(new(BindPipeMessageLevel.Error, "Unknown bind command."));
                break;
        }

        writer.Write((byte)exitCode);
        writer.Write(rebootRequired);
        writer.Write((byte)messages.Count);
        foreach (var msg in messages)
        {
            writer.Write((byte)msg.Level);
            writer.Write(msg.Text);
        }
        writer.Flush();
        await pipe.FlushAsync(cancellationToken);
    }

    static void DrainRequestPayload(BinaryReader reader, BindPipeCommand command)
    {
        try
        {
            switch (command)
            {
                case BindPipeCommand.Bind:
                    _ = reader.ReadString();
                    _ = reader.ReadString();
                    _ = reader.ReadBoolean();
                    break;
                case BindPipeCommand.Unbind:
                    _ = reader.ReadBytes(16);
                    break;
                case BindPipeCommand.UnbindAll:
                default:
                    break;
            }
        }
        catch (IOException)
        {
        }
    }
}
