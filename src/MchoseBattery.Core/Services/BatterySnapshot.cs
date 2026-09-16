namespace MchoseBattery.Core.Services;

public enum BatteryConnectionState
{
    DongleNotFound,
    HeadsetDisconnected,
    Connected,
}

public sealed record BatterySnapshot(BatteryConnectionState State, int? Percentage);
