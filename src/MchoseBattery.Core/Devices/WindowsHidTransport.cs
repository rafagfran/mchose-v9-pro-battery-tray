using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;
using MchoseBattery.Core.Protocol;

[assembly: InternalsVisibleTo("MchoseBattery.Core.Tests")]

namespace MchoseBattery.Core.Devices;

public sealed class WindowsHidTransport : IHidTransport
{
    private const int TimeoutMilliseconds = 500;
    private const int ErrorIoPending = 997;
    private const int ErrorIoIncomplete = 996;
    private const int ErrorOperationAborted = 995;
    private const int ErrorWaitTimeout = 258;
    private const uint FileFlagOverlapped = 0x40000000;
    private static readonly ConcurrentDictionary<string, byte> OutstandingPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, byte[], CancellationToken, Stopwatch, byte[]?> _exchangeCore;

    public WindowsHidTransport() : this(ExchangeCore)
    {
    }

    internal WindowsHidTransport(Func<string, byte[], CancellationToken, Stopwatch, byte[]?> exchangeCore)
    {
        _exchangeCore = exchangeCore ?? throw new ArgumentNullException(nameof(exchangeCore));
    }

    public Task<byte[]?> Exchange(HidDeviceInfo candidate, byte[] request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(request);
        if (!MchoseDevice.FindCandidates(new[] { candidate }).Any())
        {
            return Task.FromResult<byte[]?>(null);
        }

        var safeRequest = MchoseProtocol.CreateBatteryRequest();
        if (!request.AsSpan().SequenceEqual(safeRequest))
        {
            throw new ArgumentException("Only the confirmed 64-byte battery request is permitted.", nameof(request));
        }

        var path = candidate.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<byte[]?>(null);
        }

        var deadline = Stopwatch.StartNew();
        // A timed-out caller can leave cancellation cleanup pending. Reserve this path
        // until the native worker has completed, including handle and buffer cleanup.
        if (!OutstandingPaths.TryAdd(path, 0))
        {
            return Task.FromResult<byte[]?>(null);
        }

        CancellationTokenSource? nativeCancellation = null;
        Task<byte[]?> worker;
        try
        {
            nativeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            nativeCancellation.CancelAfter(Math.Max(0, TimeoutMilliseconds - (int)deadline.ElapsedMilliseconds));
            var ownedCancellation = nativeCancellation!;
            worker = Task.Run(
                () => _exchangeCore(path, safeRequest, ownedCancellation.Token, deadline),
                CancellationToken.None);
        }
        catch
        {
            try
            {
                nativeCancellation?.Dispose();
            }
            finally
            {
                OutstandingPaths.TryRemove(path, out _);
            }

            throw;
        }

        var cleanupCancellation = nativeCancellation!;
        // The caller may time out while the worker still owns a pending native operation.
        // Observe any later failure without releasing those resources on the caller thread.
        _ = worker.ContinueWith(completed =>
        {
            try
            {
                _ = completed.Exception;
                cleanupCancellation.Dispose();
            }
            finally
            {
                OutstandingPaths.TryRemove(path, out _);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        var remaining = Math.Max(0, TimeoutMilliseconds - (int)deadline.ElapsedMilliseconds);
        return AwaitByDeadline(worker, TimeSpan.FromMilliseconds(remaining), cancellationToken);
    }

    private static async Task<byte[]?> AwaitByDeadline(
        Task<byte[]?> worker, TimeSpan remaining, CancellationToken cancellationToken)
    {
        try
        {
            return await worker.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The native deadline cancelled the worker, not the caller.
            return null;
        }
    }

    private static byte[]? ExchangeCore(
        string path, byte[] request, CancellationToken cancellationToken, Stopwatch deadline)
    {
        if (!OperatingSystem.IsWindows() || deadline.ElapsedMilliseconds >= TimeoutMilliseconds)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var device = CreateFile(
            path,
            0x80000000 | 0x40000000, // GENERIC_READ | GENERIC_WRITE
            0x00000001 | 0x00000002, // FILE_SHARE_READ | FILE_SHARE_WRITE
            IntPtr.Zero,
            3, // OPEN_EXISTING
            FileFlagOverlapped,
            IntPtr.Zero);
        if (device.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open candidate HID collection.");
        }

        var buffer = IntPtr.Zero;
        var overlapped = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(64);
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<IoOverlapped>());
            var eventPointer = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            if (eventPointer == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create HID I/O event.");
            }

            using var completionEvent = new SafeWaitHandle(eventPointer, ownsHandle: true);

            Marshal.Copy(request, 0, buffer, 64);
            var written = IssueIo(device, buffer, overlapped, completionEvent, deadline, cancellationToken, writing: true);
            if (written is null)
            {
                return null;
            }

            if (written != 64)
            {
                throw new IOException("HID battery request was not fully written.");
            }

            var received = IssueIo(device, buffer, overlapped, completionEvent, deadline, cancellationToken, writing: false);
            if (received is null || received == 0)
            {
                return null;
            }

            if (received.Value > 64)
            {
                throw new IOException("HID reply exceeds the input report buffer.");
            }

            var reply = new byte[(int)received.Value];
            Marshal.Copy(buffer, reply, 0, reply.Length);
            return reply;
        }
        finally
        {
            if (overlapped != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(overlapped);
            }

            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static uint? IssueIo(
        SafeFileHandle device,
        IntPtr buffer,
        IntPtr overlapped,
        SafeWaitHandle completionEvent,
        Stopwatch deadline,
        CancellationToken cancellationToken,
        bool writing)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var remaining = TimeoutMilliseconds - (int)deadline.ElapsedMilliseconds;
        if (remaining <= 0)
        {
            return null;
        }

        if (!ResetEvent(completionEvent))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot reset HID I/O event.");
        }

        Marshal.StructureToPtr(new IoOverlapped { EventHandle = completionEvent.DangerousGetHandle() }, overlapped, false);
        var started = writing
            ? WriteFile(device, buffer, 64, IntPtr.Zero, overlapped)
            : ReadFile(device, buffer, 64, IntPtr.Zero, overlapped);
        if (!started && Marshal.GetLastWin32Error() != ErrorIoPending)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HID I/O could not start.");
        }

        // Register after starting I/O so even an already-cancelled token cancels this operation.
        using var registration = cancellationToken.Register(() =>
        {
            CancelIoEx(device, overlapped);
        });
        remaining = Math.Max(0, TimeoutMilliseconds - (int)deadline.ElapsedMilliseconds);
        if (GetOverlappedResultEx(device, overlapped, out var transferred, (uint)remaining, false))
        {
            return transferred;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorOperationAborted)
        {
            return null;
        }

        // Cancellation only requests completion. Drain on any failure before freeing
        // buffers or OVERLAPPED, including zero-wait ERROR_IO_INCOMPLETE.
        CancelIoEx(device, overlapped);
        GetOverlappedResult(device, overlapped, out _, true);
        if (error == ErrorWaitTimeout || error == ErrorIoIncomplete)
        {
            return null;
        }

        throw new Win32Exception(error, "HID I/O failed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoOverlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr EventHandle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string path, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateEvent(
        IntPtr eventAttributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, IntPtr name);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ResetEvent(SafeWaitHandle completionEvent);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        SafeFileHandle device, IntPtr buffer, uint bytesToWrite, IntPtr bytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        SafeFileHandle device, IntPtr buffer, uint bytesToRead, IntPtr bytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResultEx(
        SafeFileHandle device, IntPtr overlapped, out uint bytesTransferred,
        uint milliseconds, [MarshalAs(UnmanagedType.Bool)] bool alertable);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle device, IntPtr overlapped, out uint bytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle device, IntPtr overlapped);
}
