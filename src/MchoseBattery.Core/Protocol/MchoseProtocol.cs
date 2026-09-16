namespace MchoseBattery.Core.Protocol;

public readonly record struct BatteryReading(int Percentage, byte StatusCode);

public static class MchoseProtocol
{
    public static byte[] CreateBatteryRequest()
    {
        var frame = new byte[64];
        frame[0] = 0x55;
        frame[1] = 0x65;
        frame[2] = 0x01;
        return frame;
    }

    public static bool TryParseBatteryResponse(ReadOnlySpan<byte> frame, out BatteryReading reading)
    {
        reading = default;
        if (frame.Length < 4 || frame[0] != 0x55 || frame[1] != 0x65 || frame[2] > 100)
        {
            return false;
        }

        reading = new BatteryReading(frame[2], frame[3]);
        return true;
    }
}
