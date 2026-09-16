using MchoseBattery.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MchoseBattery.Core.Tests.Protocol;

[TestClass]
public class MchoseProtocolTests
{
    [TestMethod]
    public void CreateBatteryRequest_Is64BytesAndContainsOnlyKnownCommand()
    {
        var request = MchoseProtocol.CreateBatteryRequest();

        Assert.AreEqual(64, request.Length);
        CollectionAssert.AreEqual(new byte[] { 0x55, 0x65, 0x01 }, request[..3]);
        Assert.IsTrue(request[3..].All(value => value == 0));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(78)]
    [DataRow(100)]
    public void TryParseBatteryResponse_AcceptsBoundedSignedReply(int percentage)
    {
        var frame = new byte[64];
        frame[0] = 0x55;
        frame[1] = 0x65;
        frame[2] = (byte)percentage;
        frame[3] = 0x10;

        Assert.IsTrue(MchoseProtocol.TryParseBatteryResponse(frame, out var reading));
        Assert.AreEqual(percentage, reading.Percentage);
    }

    [TestMethod]
    public void TryParseBatteryResponse_RejectsWrongSignatureAndShortFrame()
    {
        Assert.IsFalse(MchoseProtocol.TryParseBatteryResponse(new byte[] { 0x55, 0x64, 99, 0 }, out _));
        Assert.IsFalse(MchoseProtocol.TryParseBatteryResponse(new byte[] { 0x55, 0x65, 99 }, out _));
    }
}
