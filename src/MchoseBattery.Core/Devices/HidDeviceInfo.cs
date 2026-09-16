namespace MchoseBattery.Core.Devices;

public sealed record HidDeviceInfo(
    string Path,
    int VendorId,
    int ProductId,
    string? Manufacturer,
    string? Product,
    string? SerialNumber,
    int UsagePage,
    int Usage,
    int InputReportByteLength,
    int OutputReportByteLength,
    int FeatureReportByteLength)
{
    public int VersionNumber { get; init; }
}
