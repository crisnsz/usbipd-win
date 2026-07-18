// SPDX-FileCopyrightText: 2026 crisnsz
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Usbipd.Automation;
using Windows.Win32;

namespace Usbipd;

internal sealed record UsbDevice(string InstanceId, string Description, bool IsForced,
    BusId? BusId = null, Guid? Guid = null, IPAddress? IPAddress = null, string? StubInstanceId = null)
{
    public VidPid HardwareId => VidPid.TryParseId(InstanceId, out var vidPid) ? vidPid : default;

    public static IEnumerable<UsbDevice> GetAll()
    {
        var usbDevices = new Dictionary<string, UsbDevice>(UsbipdRegistry.Instance.GetBoundDevices().Select(d => KeyValuePair.Create(d.InstanceId, new UsbDevice(d.InstanceId, d.Description, d.IsForced, d.BusId, d.PersistedGuid, d.ClientIPAddress, d.StubInstanceId))));
        foreach (var device in WindowsDevice.GetAll(PInvoke.GUID_DEVINTERFACE_USB_HUB).SelectMany(di => di.Children)
            .Where(d => !d.IsStub && !d.IsHub))
        {
            if (usbDevices.ContainsKey(device.InstanceId))
            {
                continue;
            }
            try
            {
                usbDevices[device.InstanceId] = new(
                    InstanceId: device.InstanceId,
                    Description: device.Description,
                    BusId: device.BusId,
                    IsForced: device.HasVBoxDriver);
            }
            catch (ConfigurationManagerException) { }
        }
        return usbDevices.Values;
    }
}
