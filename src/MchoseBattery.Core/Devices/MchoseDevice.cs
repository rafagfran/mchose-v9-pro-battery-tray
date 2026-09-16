namespace MchoseBattery.Core.Devices;

public static class MchoseDevice
{
    public static IEnumerable<HidDeviceInfo> FindCandidates(IEnumerable<HidDeviceInfo> devices) =>
        devices.Where(device => device.VendorId == 0x291D &&
                                device.ProductId == 0x385D &&
                                device.InputReportByteLength == 64 &&
                                device.OutputReportByteLength == 64);

    public static HidDeviceInfo? FindDevice(IEnumerable<HidDeviceInfo> devices) =>
        FindCandidates(devices).FirstOrDefault();
}
