using MchoseBattery.Core.Devices;
using MchoseBattery.Core.Diagnostics;
using MchoseBattery.Core.Protocol;

namespace MchoseBattery.Core.Services;

public sealed class BatteryService : IDisposable
{
    private readonly Func<IReadOnlyList<HidDeviceInfo>> _enumerate;
    private readonly IHidTransport _transport;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _pollCancellation = new();
    private BatterySnapshot _current = new(BatteryConnectionState.DongleNotFound, null);
    private int _pollStarted;
    private int _disposed;

    public BatteryService()
        : this(() => new HidDeviceEnumerator().Enumerate(), new WindowsHidTransport(), new AppLogger())
    {
    }

    public BatteryService(
        Func<IReadOnlyList<HidDeviceInfo>> enumerate,
        IHidTransport transport,
        IAppLogger logger,
        bool startPolling = true)
    {
        _enumerate = enumerate ?? throw new ArgumentNullException(nameof(enumerate));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (startPolling)
        {
            StartPolling();
        }
    }

    public BatterySnapshot CurrentSnapshot => Volatile.Read(ref _current);

    public event EventHandler<BatterySnapshot>? SnapshotChanged;

    public async Task<BatterySnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return CurrentSnapshot;
        }

        try
        {
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CurrentSnapshot;
        }

        try
        {
            HidDeviceInfo[] candidates;
            try
            {
                candidates = MchoseDevice.FindCandidates(_enumerate()).ToArray();
            }
            catch (Exception ex)
            {
                LogFailure($"HID enumeration failed ({ex.GetType().Name}).");
                return CurrentSnapshot;
            }

            if (candidates.Length == 0)
            {
                return Publish(new BatterySnapshot(BatteryConnectionState.DongleNotFound, null));
            }

            foreach (var candidate in candidates)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return CurrentSnapshot;
                }

                try
                {
                    var reply = await _transport.Exchange(
                        candidate, MchoseProtocol.CreateBatteryRequest(), cancellationToken).ConfigureAwait(false);
                    if (reply is null)
                    {
                        continue;
                    }

                    if (MchoseProtocol.TryParseBatteryResponse(reply, out var reading))
                    {
                        return Publish(new BatterySnapshot(BatteryConnectionState.Connected, reading.Percentage));
                    }

                    LogFailure("Invalid battery reply.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return CurrentSnapshot;
                }
                catch (Exception ex)
                {
                    LogFailure($"HID query failed ({ex.GetType().Name}).");
                }
            }

            return Publish(new BatterySnapshot(BatteryConnectionState.HeadsetDisconnected, null));
        }
        catch (Exception ex)
        {
            LogFailure($"Battery refresh failed ({ex.GetType().Name}).");
            return CurrentSnapshot;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void RequestRefresh()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _ = RefreshAsync(CancellationToken.None);
        }
    }

    public void StartPolling()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _pollStarted, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() => PollAsync(_pollCancellation.Token));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _pollCancellation.Cancel();
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogFailure($"Battery polling failed ({ex.GetType().Name}).");
        }
    }

    private BatterySnapshot Publish(BatterySnapshot snapshot)
    {
        var previous = CurrentSnapshot;
        Volatile.Write(ref _current, snapshot);
        if (previous == snapshot)
        {
            return snapshot;
        }

        LogFailure(snapshot.State == BatteryConnectionState.Connected
            ? $"Battery state changed: Connected ({snapshot.Percentage}%)."
            : $"Battery state changed: {snapshot.State}.");

        var handlers = SnapshotChanged;
        if (handlers is not null)
        {
            foreach (EventHandler<BatterySnapshot> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, snapshot);
                }
                catch (Exception ex)
                {
                    LogFailure($"Battery status callback failed ({ex.GetType().Name}).");
                }
            }
        }

        return snapshot;
    }

    private void LogFailure(string message)
    {
        try
        {
            _logger.Write(message);
        }
        catch
        {
            // A custom logger must not be able to break polling.
        }
    }
}
