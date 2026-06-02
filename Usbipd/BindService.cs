namespace Usbipd;

static class BindService
{
    public static ExitCode Bind(string instanceId, bool force,
        out bool rebootRequired, List<BindPipeMessage> messages)
    {
        rebootRequired = false;

        var device = DeviceExtensions.GetAll().FirstOrDefault(
            d => string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            messages.Add(new(BindPipeMessageLevel.Error, $"There is no USB device with instance id '{instanceId}'."));
            return ExitCode.Failure;
        }

        if (device.PersistedGuid is null)
        {
            UsbipdRegistry.Instance.Persist(device.InstanceId, device.Description);
        }

        if (force != device.IsForced && WindowsDevice.TryCreate(device.InstanceId, out var windowsDevice))
        {
            var reboot = force ? DriverTools.ForceVBoxDriver(windowsDevice) : DriverTools.UnforceVBoxDriver(windowsDevice);
            if (reboot)
            {
                rebootRequired = true;
            }
        }

        return ExitCode.Success;
    }

    public static ExitCode Unbind(Guid guid, out bool rebootRequired, List<BindPipeMessage> messages)
    {
        rebootRequired = false;

        var device = UsbipdRegistry.Instance.GetBoundDevices()
            .FirstOrDefault(d => d.PersistedGuid == guid);

        if (device is null)
        {
            messages.Add(new(BindPipeMessageLevel.Error, $"There is no device with guid '{guid:D}'."));
            return ExitCode.Failure;
        }

        UsbipdRegistry.Instance.StopSharingDevice(guid);

        if (WindowsDevice.TryCreate(device.InstanceId, out var windowsDevice))
        {
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                if (DriverTools.UnforceVBoxDriver(windowsDevice))
                {
                    rebootRequired = true;
                }
            }
            catch
            {
                messages.Add(new(BindPipeMessageLevel.Error, "Not all drivers could be restored."));
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        return ExitCode.Success;
    }

    public static ExitCode UnbindAll(out bool rebootRequired, List<BindPipeMessage> messages)
    {
        rebootRequired = false;

        UsbipdRegistry.Instance.StopSharingAllDevices();

        var reboot = false;
        var driverError = false;
        foreach (var device in WindowsDevice.GetAll(DriverDetails.Instance.ClassGuid, false)
            .Where(d => d.HasVBoxDriver && !d.IsStub))
        {
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                if (DriverTools.UnforceVBoxDriver(device))
                {
                    reboot = true;
                }
            }
            catch
            {
                driverError = true;
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        if (driverError)
        {
            messages.Add(new(BindPipeMessageLevel.Error, "Not all drivers could be restored."));
        }

        if (reboot)
        {
            rebootRequired = true;
        }

        return driverError ? ExitCode.Failure : ExitCode.Success;
    }
}
