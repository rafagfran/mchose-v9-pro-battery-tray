using MchoseBattery.Core.Devices;
using MchoseBattery.Tray;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MchoseBattery.Core.Tests.Tray;

[TestClass]
public sealed class DiagnosticsCommandTests
{
    private const string DevicePath = @"\\?\hid#vid_291d&pid_385d#test#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [DataTestMethod]
    [DataRow("--diagnose")]
    [DataRow("--list-hid")]
    [DataRow("--inspect-device")]
    public async Task ReadOnlyModes_PrintCapabilitiesWithoutSendingRequests(string mode)
    {
        var output = new StringWriter();
        var transport = new RecordingTransport();
        var command = new DiagnosticsCommand(() => new[] { Device() }, transport, output);

        var code = await command.RunAsync(mode == "--inspect-device"
            ? new[] { mode, DevicePath }
            : new[] { mode });

        Assert.AreEqual(0, code);
        Assert.AreEqual(0, transport.Requests.Count);
        StringAssert.Contains(output.ToString(), DevicePath);
        StringAssert.Contains(output.ToString(), "291D:385D");
        StringAssert.Contains(output.ToString(), "Input: 64; Output: 64; Feature: 0");
    }

    [DataTestMethod]
    [DataRow(0x291D, 0x385E, 64, 64)]
    [DataRow(0x1234, 0x385D, 64, 64)]
    [DataRow(0x291D, 0x385D, 65, 64)]
    [DataRow(0x291D, 0x385D, 64, 65)]
    public async Task Probe_RefusesUnmatchedProtocolProfiles(int vendor, int product, int input, int outputLength)
    {
        var transport = new RecordingTransport();
        var device = Device() with
        {
            VendorId = vendor, ProductId = product,
            InputReportByteLength = input, OutputReportByteLength = outputLength,
        };
        var command = new DiagnosticsCommand(() => new[] { device }, transport, new StringWriter());

        Assert.AreEqual(2, await command.RunAsync(new[] { "--probe-battery", DevicePath }));
        Assert.AreEqual(0, transport.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("vid_291d&pid_385d")]
    [DataRow(DevicePath + "-different")]
    public async Task Probe_RequiresTheEntireEnumeratedPath(string suppliedPath)
    {
        var transport = new RecordingTransport();
        var command = new DiagnosticsCommand(() => new[] { Device() }, transport, new StringWriter());

        Assert.AreEqual(2, await command.RunAsync(new[] { "--probe-battery", suppliedPath }));
        Assert.AreEqual(0, transport.Requests.Count);
    }

    [TestMethod]
    public async Task Probe_RefusesAnAmbiguousPath()
    {
        var transport = new RecordingTransport();
        var command = new DiagnosticsCommand(() => new[] { Device(), Device() }, transport, new StringWriter());

        Assert.AreEqual(2, await command.RunAsync(new[] { "--probe-battery", DevicePath }));
        Assert.AreEqual(0, transport.Requests.Count);
    }

    [TestMethod]
    public async Task Probe_PrintsRawRequestAndReplyFromTheExactSelectedCollection()
    {
        var output = new StringWriter();
        var transport = new RecordingTransport { Reply = new byte[] { 0x55, 0x65, 73, 0xA7 } };
        var command = new DiagnosticsCommand(() => new[] { Device() }, transport, output);

        Assert.AreEqual(0, await command.RunAsync(new[] { "--probe-battery", DevicePath.ToUpperInvariant() }));
        Assert.AreEqual(1, transport.Requests.Count);
        Assert.AreEqual(DevicePath, transport.Requests[0].Device.Path);
        var expected = new byte[64];
        expected[0] = 0x55;
        expected[1] = 0x65;
        expected[2] = 0x01;
        CollectionAssert.AreEqual(expected, transport.Requests[0].Request);
        StringAssert.Contains(output.ToString(), "Request (64 bytes): 55 65 01 00");
        StringAssert.Contains(output.ToString(), "Reply (4 bytes): 55 65 49 A7");
        StringAssert.Contains(output.ToString(), "Battery: 73%; status: 0xA7");
    }

    [TestMethod]
    public async Task Probe_PrintsAnInvalidReplyWithoutTreatingItAsConnected()
    {
        var output = new StringWriter();
        var transport = new RecordingTransport { Reply = new byte[] { 0x55, 0x65, 101, 0 } };
        var command = new DiagnosticsCommand(() => new[] { Device() }, transport, output);

        Assert.AreEqual(1, await command.RunAsync(new[] { "--probe-battery", DevicePath }));
        StringAssert.Contains(output.ToString(), "Reply (4 bytes): 55 65 65 00");
        StringAssert.Contains(output.ToString(), "Invalid battery reply");
    }

    [TestMethod]
    public async Task Probe_ReportsTimeoutWithoutThrowing()
    {
        var output = new StringWriter();
        var command = new DiagnosticsCommand(() => new[] { Device() }, new RecordingTransport(), output);

        Assert.AreEqual(1, await command.RunAsync(new[] { "--probe-battery", DevicePath }));
        StringAssert.Contains(output.ToString(), "Reply: <no reply>");
    }

    [TestMethod]
    public async Task InvalidArguments_DoNotEnumerateOrSendRequests()
    {
        var transport = new RecordingTransport();
        var command = new DiagnosticsCommand(
            () => throw new InvalidOperationException("Must not enumerate"), transport, new StringWriter());

        Assert.AreEqual(2, await command.RunAsync(new[] { "--probe-battery" }));
        Assert.AreEqual(2, await command.RunAsync(new[] { "--diagnose", DevicePath }));
        Assert.AreEqual(2, await command.RunAsync(new[] { "--unknown" }));
        Assert.AreEqual(0, transport.Requests.Count);
    }

    private static HidDeviceInfo Device() => new(
        DevicePath, 0x291D, 0x385D, "MCHOSE", "V9 Pro", "test-serial", 0xFF00, 1, 64, 64, 0);

    private sealed class RecordingTransport : IHidTransport
    {
        public List<(HidDeviceInfo Device, byte[] Request)> Requests { get; } = new();
        public byte[]? Reply { get; init; }

        public Task<byte[]?> Exchange(HidDeviceInfo candidate, byte[] request, CancellationToken cancellationToken)
        {
            Requests.Add((candidate, request.ToArray()));
            return Task.FromResult(Reply);
        }
    }
}
