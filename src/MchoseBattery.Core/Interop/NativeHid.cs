using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MchoseBattery.Core.Interop;

internal static class NativeHid
{
    internal const int ErrorInsufficientBuffer = 122;
    internal const int ErrorNoMoreItems = 259;
    internal const int HidpStatusSuccess = 0x00110000;
    internal const uint DigcfPresent = 0x00000002;
    internal const uint DigcfDeviceInterface = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidAttributes
    {
        public uint Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    // HidP_GetCaps writes the complete 64-byte HIDP_CAPS, including fields not used here.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct HidpCaps
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputReportByteLength;
        [FieldOffset(6)] public ushort OutputReportByteLength;
        [FieldOffset(8)] public ushort FeatureReportByteLength;
    }

    internal sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeDeviceInfoSetHandle() : base(true) { }

        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }

    [DllImport("hid.dll", ExactSpelling = true)]
    internal static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", ExactSpelling = true, SetLastError = true)]
    internal static extern SafeDeviceInfoSetHandle SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr parentWindow, uint flags);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        SafeDeviceInfoSetHandle deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref DeviceInterfaceData interfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(
        SafeDeviceInfoSetHandle deviceInfoSet, ref DeviceInterfaceData interfaceData,
        IntPtr detailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string path, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    // Access 0 permits HID metadata queries without requesting read or write I/O rights.
    internal static SafeFileHandle OpenForMetadata(string path) =>
        CreateFile(path, 0, 0x00000001 | 0x00000002, IntPtr.Zero, 3, 0, IntPtr.Zero);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_GetAttributes(SafeFileHandle device, ref HidAttributes attributes);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", ExactSpelling = true)]
    internal static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_GetManufacturerString(SafeFileHandle device, [Out] byte[] buffer, uint bufferByteLength);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_GetProductString(SafeFileHandle device, [Out] byte[] buffer, uint bufferByteLength);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_GetSerialNumberString(SafeFileHandle device, [Out] byte[] buffer, uint bufferByteLength);
}
