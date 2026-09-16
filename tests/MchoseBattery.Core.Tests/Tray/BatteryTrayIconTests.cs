using System.Drawing;
using MchoseBattery.Core.Services;
using MchoseBattery.Tray;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MchoseBattery.Core.Tests.Tray;

[TestClass]
public sealed class BatteryTrayIconTests
{
    [DataTestMethod]
    [DataRow(0, "0")]
    [DataRow(20, "20")]
    [DataRow(73, "73")]
    [DataRow(100, "100")]
    public void IconText_ShowsThePercentageWhileConnected(int percentage, string expected)
    {
        Assert.AreEqual(expected, BatteryTrayIcon.GetIconText(Connected(percentage)));
    }

    [TestMethod]
    public void IconText_DistinguishesTheUnavailableStates()
    {
        Assert.AreEqual("?", BatteryTrayIcon.GetIconText(new(BatteryConnectionState.DongleNotFound, null)));
        Assert.AreEqual("--", BatteryTrayIcon.GetIconText(new(BatteryConnectionState.HeadsetDisconnected, null)));
    }

    [TestMethod]
    public void BackgroundColor_FollowsTheChargeThresholds()
    {
        var low = BatteryTrayIcon.GetBackgroundColor(Connected(20));
        var medium = BatteryTrayIcon.GetBackgroundColor(Connected(40));
        var high = BatteryTrayIcon.GetBackgroundColor(Connected(100));
        var unknown = BatteryTrayIcon.GetBackgroundColor(new(BatteryConnectionState.HeadsetDisconnected, null));

        Assert.AreEqual(low, BatteryTrayIcon.GetBackgroundColor(Connected(0)));
        Assert.AreEqual(medium, BatteryTrayIcon.GetBackgroundColor(Connected(21)));
        Assert.AreEqual(high, BatteryTrayIcon.GetBackgroundColor(Connected(41)));
        Assert.AreEqual(unknown, BatteryTrayIcon.GetBackgroundColor(new(BatteryConnectionState.DongleNotFound, null)));
        CollectionAssert.AllItemsAreUnique(new[] { low, medium, high, unknown });
    }

    [TestMethod]
    public void ForegroundColor_StaysReadableOnEveryBackground()
    {
        foreach (var snapshot in new[]
                 {
                     Connected(10), Connected(30), Connected(90),
                     new BatterySnapshot(BatteryConnectionState.DongleNotFound, null),
                 })
        {
            var background = BatteryTrayIcon.GetBackgroundColor(snapshot);
            var foreground = BatteryTrayIcon.GetForegroundColor(background);
            Assert.IsTrue(Math.Abs(background.GetBrightness() - foreground.GetBrightness()) > 0.45f,
                $"{background} and {foreground} do not contrast.");
        }
    }

    [DataTestMethod]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(32)]
    public void Create_RendersAnIconOfTheRequestedSizeForEveryState(int size)
    {
        foreach (var snapshot in new[]
                 {
                     Connected(0), Connected(100),
                     new BatterySnapshot(BatteryConnectionState.HeadsetDisconnected, null),
                     new BatterySnapshot(BatteryConnectionState.DongleNotFound, null),
                 })
        {
            using var icon = BatteryTrayIcon.Create(snapshot, size);
            Assert.AreEqual(size, icon.Width);
            Assert.AreEqual(size, icon.Height);
        }
    }

    [TestMethod]
    public void Create_KeepsUnreasonableSizesWithinTrayBounds()
    {
        using var tiny = BatteryTrayIcon.Create(Connected(50), 0);
        using var huge = BatteryTrayIcon.Create(Connected(50), 4096);
        Assert.IsTrue(tiny.Width is >= 16 and <= 64);
        Assert.IsTrue(huge.Width is >= 16 and <= 64);
    }

    private static BatterySnapshot Connected(int percentage) => new(BatteryConnectionState.Connected, percentage);
}
