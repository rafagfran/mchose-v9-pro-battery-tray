using MchoseBattery.Core.Devices;
using MchoseBattery.Core.Diagnostics;
using MchoseBattery.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MchoseBattery.Core.Tests.Services;

[TestClass]
public class BatteryServiceTests
{
    private static readonly HidDeviceInfo Candidate =
        new("candidate", 0x291D, 0x385D, null, null, null, 0, 0, 64, 64, 0);

    [TestMethod]
    public async Task RefreshAsync_MapsTimeoutWithCandidateToHeadsetDisconnected()
    {
        using var service = CreateService(new[] { Candidate }, (_, _) => Task.FromResult<byte[]?>(null));

        var snapshot = await service.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(BatteryConnectionState.HeadsetDisconnected, snapshot.State);
        Assert.IsNull(snapshot.Percentage);
    }

    [TestMethod]
    public async Task RefreshAsync_MapsNoCandidatesToDongleNotFoundWithoutQuery()
    {
        var queried = false;
        var nonCandidate = Candidate with { ProductId = 0x385E };
        using var service = CreateService(new[] { nonCandidate }, (_, _) =>
        {
            queried = true;
            return Task.FromResult<byte[]?>(null);
        });

        var snapshot = await service.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(BatteryConnectionState.DongleNotFound, snapshot.State);
        Assert.IsFalse(queried);
    }

    [TestMethod]
    public async Task RefreshAsync_MapsValidatedReplyToConnected()
    {
        var reply = ValidReply(78);
        using var service = CreateService(new[] { Candidate }, (_, _) => Task.FromResult<byte[]?>(reply));

        var snapshot = await service.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(BatteryConnectionState.Connected, snapshot.State);
        Assert.AreEqual(78, snapshot.Percentage);
    }

    [TestMethod]
    public async Task RefreshAsync_RejectsMalformedReplyAndTriesNextCandidate()
    {
        var second = Candidate with { Path = "second" };
        using var service = CreateService(new[] { Candidate, second }, (device, _) =>
            Task.FromResult<byte[]?>(device.Path == "candidate" ? new byte[] { 0x55, 0x64, 90, 0 } : ValidReply(42)));

        var snapshot = await service.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(BatteryConnectionState.Connected, snapshot.State);
        Assert.AreEqual(42, snapshot.Percentage);
    }

    [TestMethod]
    public async Task RefreshAsync_NativeFailureDoesNotEscapeAndKeepsLastSnapshot()
    {
        var failEnumeration = false;
        var logger = new FakeLogger();
        using var service = new BatteryService(
            () => failEnumeration ? throw new InvalidOperationException("discovery failed") : new[] { Candidate },
            new FakeTransport((_, _) => Task.FromResult<byte[]?>(ValidReply(65))),
            logger,
            startPolling: false);
        var connected = await service.RefreshAsync(CancellationToken.None);
        failEnumeration = true;

        var afterFailure = await service.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(connected, afterFailure);
        Assert.IsTrue(logger.Messages.Any(message => message.Contains("enumeration failed", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task RefreshAsync_TransportFailureMapsToDisconnectedAndIsLogged()
    {
        var logger = new FakeLogger();
        using var service = new BatteryService(
            () => new[] { Candidate },
            new FakeTransport((_, _) => throw new IOException("device removed")),
            logger,
            startPolling: false);

        var snapshot = await service.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(BatteryConnectionState.HeadsetDisconnected, snapshot.State);
        Assert.IsTrue(logger.Messages.Any(message => message.Contains("query failed", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task RefreshAsync_StableSuccessfulPollDoesNotAddLogEntry()
    {
        var logger = new FakeLogger();
        using var service = new BatteryService(
            () => new[] { Candidate },
            new FakeTransport((_, _) => Task.FromResult<byte[]?>(ValidReply(65))),
            logger,
            startPolling: false);

        await service.RefreshAsync(CancellationToken.None);
        var firstCount = logger.Messages.Count;
        await service.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(1, firstCount);
        Assert.AreEqual(firstCount, logger.Messages.Count);
    }

    [TestMethod]
    public async Task RefreshAsync_SerializesConcurrentHidQueries()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maxActive = 0;
        using var service = CreateService(new[] { Candidate }, async (_, _) =>
        {
            var now = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, now);
            firstEntered.TrySetResult();
            await releaseFirst.Task;
            Interlocked.Decrement(ref active);
            return ValidReply(51);
        });

        var first = service.RefreshAsync(CancellationToken.None);
        await firstEntered.Task;
        var second = service.RefreshAsync(CancellationToken.None);
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);

        Assert.AreEqual(1, maxActive);
    }

    [TestMethod]
    public async Task RequestRefresh_PublishesChangedSnapshot()
    {
        var observed = new TaskCompletionSource<BatterySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = CreateService(new[] { Candidate }, (_, _) => Task.FromResult<byte[]?>(ValidReply(33)));
        service.SnapshotChanged += (_, snapshot) => observed.TrySetResult(snapshot);

        service.RequestRefresh();
        var snapshot = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(BatteryConnectionState.Connected, snapshot.State);
        Assert.AreEqual(33, snapshot.Percentage);
    }

    [TestMethod]
    public async Task RefreshAsync_CancellationDoesNotEscape()
    {
        using var service = CreateService(new[] { Candidate }, (_, _) => Task.FromResult<byte[]?>(ValidReply(10)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var snapshot = await service.RefreshAsync(cancellation.Token);

        Assert.AreEqual(BatteryConnectionState.DongleNotFound, snapshot.State);
    }

    private static BatteryService CreateService(
        IReadOnlyList<HidDeviceInfo> devices,
        Func<HidDeviceInfo, CancellationToken, Task<byte[]?>> exchange)
    {
        return new BatteryService(() => devices, new FakeTransport(exchange), new FakeLogger(), startPolling: false);
    }

    private static byte[] ValidReply(byte percentage)
    {
        var reply = new byte[64];
        reply[0] = 0x55;
        reply[1] = 0x65;
        reply[2] = percentage;
        return reply;
    }

    private sealed class FakeTransport(Func<HidDeviceInfo, CancellationToken, Task<byte[]?>> exchange) : IHidTransport
    {
        public Task<byte[]?> Exchange(HidDeviceInfo candidate, byte[] request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(64, request.Length);
            CollectionAssert.AreEqual(new byte[] { 0x55, 0x65, 0x01 }, request[..3]);
            Assert.IsTrue(request[3..].All(value => value == 0));
            return exchange(candidate, cancellationToken);
        }
    }

    private sealed class FakeLogger : IAppLogger
    {
        public List<string> Messages { get; } = new();
        public void Write(string message) => Messages.Add(message);
    }
}

[TestClass]
public class WindowsHidTransportTests
{
    private static readonly HidDeviceInfo Candidate =
        new("not-a-real-device", 0x291D, 0x385D, null, null, null, 0, 0, 64, 64, 0);

    [TestMethod]
    public async Task Exchange_RefusesNonCandidateWithoutOpeningItsPath()
    {
        var transport = new WindowsHidTransport();
        var nonCandidate = Candidate with { OutputReportByteLength = 32 };

        var reply = await transport.Exchange(nonCandidate, new byte[] { 0x55, 0x65, 0x01 }.Concat(new byte[61]).ToArray(), CancellationToken.None);

        Assert.IsNull(reply);
    }

    [TestMethod]
    public void Exchange_RefusesAnyRequestOtherThanConfirmedBatteryFrame()
    {
        var transport = new WindowsHidTransport();

        Assert.ThrowsException<ArgumentException>(() => transport.Exchange(Candidate, new byte[64], CancellationToken.None));
    }
}

[TestClass]
public class AppLoggerTests
{
    [TestMethod]
    public void Write_CapsFileAndRetainsNewestEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MchoseBatteryTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "test.log");
        try
        {
            var logger = new AppLogger(path);
            for (var index = 0; index < 1600; index++)
            {
                logger.Write($"entry-{index:D4} {new string('x', 180)}");
            }

            var text = File.ReadAllText(path);
            Assert.IsTrue(new FileInfo(path).Length <= 256 * 1024);
            StringAssert.Contains(text, "entry-1599");
            Assert.IsFalse(text.Contains("entry-0000", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
