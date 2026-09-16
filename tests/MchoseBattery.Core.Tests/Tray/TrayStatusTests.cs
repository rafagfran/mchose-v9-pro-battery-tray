using MchoseBattery.Core.Services;
using MchoseBattery.Tray;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MchoseBattery.Core.Tests.Tray;

[TestClass]
public sealed class TrayStatusTests
{
    [TestMethod]
    public void StatusText_UsesTheCurrentConnectionState()
    {
        Assert.AreEqual("MCHOSE V9 Pro: 73%",
            TrayApplicationContext.GetStatusText(new(BatteryConnectionState.Connected, 73)));
        Assert.AreEqual("Headset desconectado",
            TrayApplicationContext.GetStatusText(new(BatteryConnectionState.HeadsetDisconnected, null)));
        Assert.AreEqual("Dongle não encontrado",
            TrayApplicationContext.GetStatusText(new(BatteryConnectionState.DongleNotFound, null)));
    }
}
