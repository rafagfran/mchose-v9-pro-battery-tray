using MchoseBattery.Core.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MchoseBattery.Core.Tests.Devices;

[TestClass]
public class MchoseDeviceTests
{
    [TestMethod]
    public void FindCandidates_RequiresKnownVidPidAnd64ByteInputOutputReports()
    {
        var matching = new HidDeviceInfo("matching", 0x291D, 0x385D, null, null, null, 0, 0, 64, 64, 0);
        var devices = new[]
        {
            new HidDeviceInfo("wrong vendor", 0x291E, 0x385D, null, null, null, 0, 0, 64, 64, 0),
            new HidDeviceInfo("wrong product", 0x291D, 0x385E, null, null, null, 0, 0, 64, 64, 0),
            new HidDeviceInfo("short input", 0x291D, 0x385D, null, null, null, 0, 0, 32, 64, 0),
            matching,
            new HidDeviceInfo("short output", 0x291D, 0x385D, null, null, null, 0, 0, 64, 32, 0),
        };

        CollectionAssert.AreEqual(new[] { matching }, MchoseDevice.FindCandidates(devices).ToArray());
    }

    [TestMethod]
    public void FindDevice_ReturnsFirstCandidateInInventoryOrder()
    {
        var first = new HidDeviceInfo("first", 0x291D, 0x385D, null, null, null, 0, 0, 64, 64, 0);
        var second = first with { Path = "second" };
        var devices = new[] { first with { InputReportByteLength = 32 }, first, second };

        Assert.AreEqual(first, MchoseDevice.FindDevice(devices));
        Assert.IsNull(MchoseDevice.FindDevice(Array.Empty<HidDeviceInfo>()));
    }
}
