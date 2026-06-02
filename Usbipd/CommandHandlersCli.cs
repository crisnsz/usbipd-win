// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Usbipd.Automation;
using static Usbipd.ConsoleTools;

namespace Usbipd;

sealed partial class CommandHandlers : ICommandHandlers
{
    static List<Device> GetDevicesByHardwareId(VidPid vidPid, bool connectedOnly, IConsole console)
    {
        if (!CheckNoStub(vidPid, console))
        {
            return [];
        }
        var filtered = new List<Device>();
        var devices = DeviceExtensions.GetAll().Where(d => (d.HardwareId == vidPid) && (!connectedOnly || d.BusId.HasValue));
        foreach (var device in devices)
        {
            if (device.BusId.HasValue)
            {
                if (device.BusId.Value.IsIncompatibleHub)
                {
                    console.ReportWarning($"Ignoring device with hardware-id '{vidPid}' connected to an incompatible hub.");
                }
                else
                {
                    console.ReportInfo($"Device with hardware-id '{vidPid}' found at busid '{device.BusId}'.");
                    filtered.Add(device);
                }
            }
            else if (device.PersistedGuid.HasValue)
            {
                console.ReportInfo($"Persisted device with hardware-id '{vidPid}' found at guid '{device.PersistedGuid.Value:D}'.");
                filtered.Add(device);
            }
        }
        if (filtered.Count == 0)
        {
            console.ReportError($"No devices found with hardware-id '{vidPid}'.");
        }
        return filtered;
    }

    static BusId? GetBusIdByHardwareId(VidPid vidPid, IConsole console)
    {
        try
        {
            var device = GetDevicesByHardwareId(vidPid, true, console).SingleOrDefault();
            if (device is null)
            {
                // Already reported.
                return null;
            }
            return device.BusId;
        }
        catch (InvalidOperationException)
        {
            console.ReportError($"Multiple devices with hardware-id '{vidPid}' were found; disambiguate by using '--busid'.");
            return null;
        }
    }

    Task<ExitCode> ICommandHandlers.License(IConsole console, CancellationToken cancellationToken)
    {
#pragma warning disable CA1849 // Call async methods when in an async method
        // 70 leads (approximately) to the GPL default.
        var width = console.IsOutputRedirected ? 70 : console.WindowWidth;
        foreach (var line in Wrap($"""
            {Program.Product} {GitVersionInformation.MajorMinorPatch}
            {Program.Copyright}

            This program is free software: you can redistribute it and/or modify \
            it under the terms of the GNU General Public License as published by \
            the Free Software Foundation, version 3.

            This program is distributed in the hope that it will be useful, \
            but WITHOUT ANY WARRANTY; without even the implied warranty of \
            MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the \
            GNU General Public License for more details.

            You should have received a copy of the GNU General Public License \
            along with this program. If not, see <https://www.gnu.org/licenses/>.

            """.Unwrap()
            , width))
        {
            console.Out.WriteLine(line);
        }
        return Task.FromResult(ExitCode.Success);
#pragma warning restore CA1849 // Call async methods when in an async method
    }

    static string GetDescription(Device device, bool usbIds)
    {
        if (usbIds)
        {
            var (vendor, product) = device.HardwareId.Descriptions;
            return vendor is not null
                ? $"{vendor}, {product ?? WindowsDevice.UnknownDescription}"
                : WindowsDevice.UnknownDescription;
        }
        else
        {
            return device.Description;
        }
    }

    Task<ExitCode> ICommandHandlers.List(bool usbIds, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

#pragma warning disable CA1849 // Call async methods when in an async method
        var allDevices = DeviceExtensions.GetAll().ToList();
        console.Out.WriteLine("Connected:");
        console.Out.WriteLine($"{"BUSID",-5}  {"VID:PID",-9}  {"DEVICE",-60}  STATE");
        foreach (var device in allDevices.Where(d => d.BusId.HasValue).OrderBy(d => d.BusId.GetValueOrDefault()))
        {
            Debug.Assert(device.BusId.HasValue);
            var state = device.ClientIPAddress is not null ? "Attached"
                : device.PersistedGuid is not null ? device.IsForced ? "Shared (forced)" : "Shared"
                : device.BusId.Value.IsIncompatibleHub ? "Incompatible hub"
                : Policy.IsAutoBindAllowed(device) ? "Allowed" : "Not shared";
            console.Out.Write($"{(device.BusId.Value.IsIncompatibleHub ? string.Empty : device.BusId.Value),-5}  ");
            console.Out.Write($"{device.HardwareId,-9}  ");
            console.WriteTruncated(GetDescription(device, usbIds), 60, true);
            console.Out.WriteLine($"  {state}");
        }
        console.Out.WriteLine();

        console.Out.WriteLine("Persisted:");
        console.Out.WriteLine($"{"GUID",-36}  DEVICE");
        foreach (var device in allDevices.Where(d => !d.BusId.HasValue && d.PersistedGuid.HasValue).OrderBy(d => d.PersistedGuid.GetValueOrDefault()))
        {
            Debug.Assert(device.PersistedGuid.HasValue);
            console.Out.Write($"{device.PersistedGuid.Value,-36:D}  ");
            console.WriteTruncated(GetDescription(device, usbIds), 60, false);
            console.Out.WriteLine();
        }
        console.Out.WriteLine();

        _ = console.CheckAndReportServerRunning(false);
        console.ReportIfForceNeeded();
        return Task.FromResult(ExitCode.Success);
#pragma warning restore CA1849 // Call async methods when in an async method
    }

    static void RelayMessages(IEnumerable<BindPipeMessage> messages, IConsole console)
    {
        foreach (var msg in messages)
        {
            switch (msg.Level)
            {
                case BindPipeMessageLevel.Info:
                    console.ReportInfo(msg.Text);
                    break;
                case BindPipeMessageLevel.Warning:
                    console.ReportWarning(msg.Text);
                    break;
                case BindPipeMessageLevel.Error:
                    console.ReportError(msg.Text);
                    break;
            }
        }
    }

    static async Task<ExitCode> BindAsync(BusId busId, bool force, IConsole console, CancellationToken cancellationToken)
    {
        var device = DeviceExtensions.GetAll().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return ExitCode.Failure;
        }
        if (device.PersistedGuid.HasValue && (force == device.IsForced))
        {
            // Not an error, just let the user know they just executed a no-op.
            console.ReportInfo($"Device with busid '{busId}' was already shared.");
            if (!device.IsForced)
            {
                console.ReportIfForceNeeded();
            }
            return ExitCode.Success;
        }

        ExitCode exitCode;
        bool rebootRequired;

        if (UsbipdRegistry.Instance.HasWriteAccess)
        {
            var messages = new List<BindPipeMessage>();
            exitCode = BindService.Bind(device.InstanceId, device.Description, force, out rebootRequired, messages);
            RelayMessages(messages, console);
        }
        else
        {
            exitCode = await BindPipeClient.BindAsync(device.InstanceId, device.Description, force, console, cancellationToken);
            rebootRequired = false;
        }

        if (exitCode == ExitCode.Success)
        {
            if (rebootRequired)
            {
                console.ReportRebootRequired();
            }
            if (!force)
            {
                console.ReportIfForceNeeded();
            }
            _ = console.CheckAndReportServerRunning(false);
        }

        return exitCode;
    }

    Task<ExitCode> ICommandHandlers.Bind(BusId busId, bool force, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        return BindAsync(busId, force, console, cancellationToken);
    }

    Task<ExitCode> ICommandHandlers.Bind(VidPid vidPid, bool force, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        return GetBusIdByHardwareId(vidPid, console) is BusId busId
            ? BindAsync(busId, force, console, cancellationToken)
            : Task.FromResult(ExitCode.Failure);
    }

    static async Task<ExitCode> UnbindBusIdAsync(BusId busId, IConsole console, CancellationToken cancellationToken)
    {
        var device = DeviceExtensions.GetAll().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return ExitCode.Failure;
        }
        if (device.PersistedGuid is null)
        {
            // Not an error, just let the user know they just executed a no-op.
            console.ReportInfo($"Device with busid '{busId}' was already not shared.");
            return ExitCode.Success;
        }

        return await UnbindGuidAsync(device.PersistedGuid.Value, console, cancellationToken);
    }

    static async Task<ExitCode> UnbindGuidAsync(Guid guid, IConsole console, CancellationToken cancellationToken)
    {
        ExitCode exitCode;
        bool rebootRequired;

        if (UsbipdRegistry.Instance.HasWriteAccess)
        {
            var messages = new List<BindPipeMessage>();
            exitCode = BindService.Unbind(guid, out rebootRequired, messages);
            RelayMessages(messages, console);
        }
        else
        {
            exitCode = await BindPipeClient.UnbindAsync(guid, console, cancellationToken);
            rebootRequired = false;
        }

        if (exitCode == ExitCode.Success && rebootRequired)
        {
            console.ReportRebootRequired();
        }

        return exitCode;
    }

    Task<ExitCode> ICommandHandlers.Unbind(BusId busId, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        return UnbindBusIdAsync(busId, console, cancellationToken);
    }

    Task<ExitCode> ICommandHandlers.Unbind(Guid guid, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        var device = UsbipdRegistry.Instance.GetBoundDevices().SingleOrDefault(d => d.PersistedGuid.HasValue && d.PersistedGuid.Value == guid);
        if (device is null)
        {
            console.ReportError($"There is no device with guid '{guid:D}'.");
            return Task.FromResult(ExitCode.Failure);
        }

        return UnbindGuidAsync(guid, console, cancellationToken);
    }

    Task<ExitCode> ICommandHandlers.Unbind(VidPid vidPid, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        return UnbindMultipleAsync(GetDevicesByHardwareId(vidPid, false, console), console, cancellationToken);
    }

    static async Task<ExitCode> UnbindMultipleAsync(List<Device> devices, IConsole console, CancellationToken cancellationToken)
    {
        if (devices.Count == 0)
        {
            return ExitCode.Failure;
        }

        var overallExit = ExitCode.Success;
        foreach (var device in devices)
        {
            if (device.PersistedGuid is null)
            {
                continue;
            }
            var result = await UnbindGuidAsync(device.PersistedGuid.Value, console, cancellationToken);
            if (result != ExitCode.Success)
            {
                overallExit = result;
            }
        }
        return overallExit;
    }

    async Task<ExitCode> ICommandHandlers.UnbindAll(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        ExitCode exitCode;
        bool rebootRequired;

        if (UsbipdRegistry.Instance.HasWriteAccess)
        {
            var messages = new List<BindPipeMessage>();
            exitCode = BindService.UnbindAll(out rebootRequired, messages);
            RelayMessages(messages, console);
        }
        else
        {
            exitCode = await BindPipeClient.UnbindAllAsync(console, cancellationToken);
            rebootRequired = false;
        }

        if (exitCode == ExitCode.Success && rebootRequired)
        {
            console.ReportRebootRequired();
        }

        return exitCode;
    }

    async Task<ExitCode> ICommandHandlers.AttachWsl(BusId busId, bool autoAttach, bool unplugged, string? distribution, IPAddress? hostAddress,
        IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        var device = DeviceExtensions.GetAll().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
        if (device is null)
        {
            if (!autoAttach || !unplugged)
            {
                console.ReportError($"There is no device with busid '{busId}'.");
                return ExitCode.Failure;
            }
            // user requested auto-attach, even if a device is currently not plugged in
        }
        else
        {
            if (!device.PersistedGuid.HasValue && !Policy.IsAutoBindAllowed(device))
            {
                console.ReportError($"Device is not shared; run 'usbipd bind --busid {busId}' first.");
                return ExitCode.Failure;
            }
            // We allow auto-attach on devices that are already attached.
            if (!autoAttach && (device.ClientIPAddress is not null))
            {
                console.ReportError($"Device with busid '{busId}' is already attached to a client.");
                return ExitCode.Failure;
            }
        }

        return console.CheckAndReportServerRunning(true)
            ? await Wsl.Attach(busId, autoAttach, distribution, hostAddress, console, cancellationToken)
            : ExitCode.Failure;
    }

    async Task<ExitCode> ICommandHandlers.AttachWsl(VidPid vidPid, bool autoAttach, string? distribution, IPAddress? hostAddress,
        IConsole console, CancellationToken cancellationToken)
    {
        return GetBusIdByHardwareId(vidPid, console) is BusId busId
            ? await ((ICommandHandlers)this).AttachWsl(busId, autoAttach, false, distribution, hostAddress, console, cancellationToken)
            : ExitCode.Failure;
    }

    static ExitCode Detach(IEnumerable<Device> devices, IConsole console)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        var error = false;
        foreach (var device in devices)
        {
            if (!device.PersistedGuid.HasValue || device.ClientIPAddress is null)
            {
                // Not an error, just let the user know they just executed a no-op.
                console.ReportInfo($"Device with busid '{device.BusId}' was already not attached.");
                continue;
            }
            if (!UsbipdRegistry.Instance.SetDeviceAsDetached(device.PersistedGuid.Value))
            {
                console.ReportError($"Failed to detach device with busid '{device.BusId}'.");
                error = true;
            }
        }
        return error ? ExitCode.Failure : ExitCode.Success;
    }

    Task<ExitCode> ICommandHandlers.Detach(BusId busId, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        var device = DeviceExtensions.GetAll().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Detach([device], console));
    }

    Task<ExitCode> ICommandHandlers.Detach(VidPid vidPid, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        var devices = GetDevicesByHardwareId(vidPid, true, console);
        if (devices.Count == 0)
        {
            // This would result in a no-op, which may not be what the user intended.
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Detach(devices, console));
    }

    Task<ExitCode> ICommandHandlers.DetachAll(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        if (!UsbipdRegistry.Instance.SetAllDevicesAsDetached())
        {
            console.ReportError($"Failed to detach one or more devices.");
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(ExitCode.Success);
    }

    async Task<ExitCode> ICommandHandlers.State(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        console.SetError(TextWriter.Null);

        var state = new State()
        {
            Devices = DeviceExtensions.GetAll().OrderBy(d => d.InstanceId).ToList(),
        };

        var context = new StateSerializerContext(new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        });
        var json = JsonSerializer.Serialize(state, context.State);

        await console.Out.WriteAsync(json);
        return ExitCode.Success;
    }

    Task<ExitCode> ICommandHandlers.PolicyAdd(PolicyRule rule, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        if (UsbipdRegistry.Instance.GetPolicyRules().FirstOrDefault(r => r.Value == rule) is var existingRule && existingRule.Key != default)
        {
            console.ReportError($"Policy rule already exists with guid '{existingRule.Key:D}'.");
            return Task.FromResult(ExitCode.Failure);
        }

        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }

        var guid = UsbipdRegistry.Instance.AddPolicyRule(rule);
        console.ReportInfo($"Policy rule created with guid '{guid:D}'.");
        return Task.FromResult(ExitCode.Success);
    }

    Task<ExitCode> ICommandHandlers.PolicyList(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

#pragma warning disable CA1849 // Call async methods when in an async method
        var policyRules = UsbipdRegistry.Instance.GetPolicyRules();
        console.Out.WriteLine("Policy rules:");
        console.Out.WriteLine($"{"GUID",-36}  {"EFFECT",-6}  {"OPERATION",-9}  {"BUSID",-5}  {"VID:PID",-9}");
        foreach (var rule in policyRules)
        {
            console.Out.Write($"{rule.Key,-36}  ");
            console.Out.Write($"{rule.Value.Effect,-6}  ");
            console.Out.Write($"{rule.Value.Operation,-9}  ");
            switch (rule.Value.Operation)
            {
                case PolicyRuleOperation.AutoBind:
                    var autoBind = (PolicyRuleAutoBind)rule.Value;
                    console.Out.Write($"{(autoBind.BusId.HasValue ? autoBind.BusId.Value : string.Empty),-5}  ");
                    console.Out.Write($"{(autoBind.HardwareId.HasValue ? autoBind.HardwareId.Value : string.Empty),-9}");
                    break;
                default:
                    throw new UnexpectedResultException();
            }
            console.Out.WriteLine();
        }
        console.Out.WriteLine();
        return Task.FromResult(ExitCode.Success);
#pragma warning restore CA1849 // Call async methods when in an async method
    }

    Task<ExitCode> ICommandHandlers.PolicyRemove(Guid guid, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        if (!UsbipdRegistry.Instance.GetPolicyRules().ContainsKey(guid))
        {
            console.ReportError($"There is no policy rule with guid '{guid:D}'.");
            return Task.FromResult(ExitCode.Failure);
        }

        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }

        UsbipdRegistry.Instance.RemovePolicyRule(guid);
        return Task.FromResult(ExitCode.Success);
    }

    Task<ExitCode> ICommandHandlers.PolicyRemoveAll(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }

        UsbipdRegistry.Instance.RemovePolicyRuleAll();
        return Task.FromResult(ExitCode.Success);
    }
}
