namespace MchoseBattery.Core.Devices;

public interface IHidTransport
{
    Task<byte[]?> Exchange(HidDeviceInfo candidate, byte[] request, CancellationToken cancellationToken);
}
