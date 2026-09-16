using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using MchoseBattery.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace MchoseBattery.Core.Devices;

public sealed class HidDeviceEnumerator
{
    public IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<HidDeviceInfo>();
        }

        NativeHid.HidD_GetHidGuid(out var hidGuid);
        using var deviceInfoSet = NativeHid.SetupDiGetClassDevs(
            ref hidGuid, IntPtr.Zero, IntPtr.Zero, NativeHid.DigcfPresent | NativeHid.DigcfDeviceInterface);
        if (deviceInfoSet.IsInvalid)
        {
            return Array.Empty<HidDeviceInfo>();
        }

        var devices = new List<HidDeviceInfo>();
        for (uint index = 0; ; index++)
        {
            var interfaceData = new NativeHid.DeviceInterfaceData
            {
                Size = (uint)Marshal.SizeOf<NativeHid.DeviceInterfaceData>(),
            };

            if (!NativeHid.SetupDiEnumDeviceInterfaces(
                    deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == NativeHid.ErrorNoMoreItems)
                {
                    break;
                }

                throw new Win32Exception(error, "Failed to enumerate HID device interfaces.");
            }

            var path = GetDevicePath(deviceInfoSet, ref interfaceData);
            if (path is not null)
            {
                devices.Add(Inspect(path));
            }
        }

        return devices;
    }

    private static string? GetDevicePath(
        NativeHid.SafeDeviceInfoSetHandle deviceInfoSet, ref NativeHid.DeviceInterfaceData interfaceData)
    {
        var sized = NativeHid.SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
        if (sized || Marshal.GetLastWin32Error() != NativeHid.ErrorInsufficientBuffer ||
            requiredSize < 6 || requiredSize > int.MaxValue)
        {
            return null;
        }

        IntPtr detailData = IntPtr.Zero;
        try
        {
            detailData = Marshal.AllocHGlobal((int)requiredSize);
            // The Unicode DevicePath starts at byte offset 4; cbSize is 8 on x64, 6 on x86.
            Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeHid.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet, ref interfaceData, detailData, requiredSize, out _, IntPtr.Zero))
            {
                return null;
            }

            return Marshal.PtrToStringUni(IntPtr.Add(detailData, 4));
        }
        finally
        {
            if (detailData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(detailData);
            }
        }
    }

    private static HidDeviceInfo Inspect(string path)
    {
        using var device = NativeHid.OpenForMetadata(path);
        if (device.IsInvalid)
        {
            return new HidDeviceInfo(path, 0, 0, null, null, null, 0, 0, 0, 0, 0);
        }

        var attributes = new NativeHid.HidAttributes
        {
            Size = (uint)Marshal.SizeOf<NativeHid.HidAttributes>(),
        };
        var hasAttributes = NativeHid.HidD_GetAttributes(device, ref attributes);

        var caps = default(NativeHid.HidpCaps);
        IntPtr preparsedData = IntPtr.Zero;
        try
        {
            if (NativeHid.HidD_GetPreparsedData(device, out preparsedData) &&
                preparsedData != IntPtr.Zero)
            {
                if (NativeHid.HidP_GetCaps(preparsedData, out var queriedCaps) == NativeHid.HidpStatusSuccess)
                {
                    caps = queriedCaps;
                }
            }
        }
        finally
        {
            if (preparsedData != IntPtr.Zero)
            {
                NativeHid.HidD_FreePreparsedData(preparsedData);
            }
        }

        return new HidDeviceInfo(
            path,
            hasAttributes ? attributes.VendorId : 0,
            hasAttributes ? attributes.ProductId : 0,
            ReadString(device, NativeHid.HidD_GetManufacturerString),
            ReadString(device, NativeHid.HidD_GetProductString),
            ReadString(device, NativeHid.HidD_GetSerialNumberString),
            caps.UsagePage,
            caps.Usage,
            caps.InputReportByteLength,
            caps.OutputReportByteLength,
            caps.FeatureReportByteLength)
        {
            VersionNumber = hasAttributes ? attributes.VersionNumber : 0,
        };
    }

    private static string? ReadString(
        SafeFileHandle device, Func<SafeFileHandle, byte[], uint, bool> query)
    {
        var buffer = new byte[256];
        if (!query(device, buffer, (uint)buffer.Length))
        {
            return null;
        }

        var value = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        var terminator = value.IndexOf('\0');
        return terminator >= 0 ? value[..terminator] : value;
    }
}
