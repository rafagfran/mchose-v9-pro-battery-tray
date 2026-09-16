using MchoseBattery.Core.Devices;
using MchoseBattery.Core.Protocol;

namespace MchoseBattery.Tray;

public sealed class DiagnosticsCommand
{
    private readonly Func<IReadOnlyList<HidDeviceInfo>> _enumerate;
    private readonly IHidTransport _transport;
    private readonly TextWriter _output;

    public DiagnosticsCommand(Func<IReadOnlyList<HidDeviceInfo>> enumerate, IHidTransport transport, TextWriter output)
    {
        _enumerate = enumerate ?? throw new ArgumentNullException(nameof(enumerate));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var list = args.Length == 1 && args[0] is "--diagnose" or "--list-hid";
        var inspect = args.Length == 2 && args[0] == "--inspect-device";
        var probe = args.Length == 2 && args[0] == "--probe-battery";
        if (!list && !inspect && !probe)
        {
            _output.WriteLine("Usage: MchoseBattery.exe --diagnose | --list-hid | --inspect-device <path> | --probe-battery <path>");
            return 2;
        }

        if (!list && (!args[1].StartsWith(@"\\?\hid#", StringComparison.OrdinalIgnoreCase) ||
                      args[1].IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
        {
            _output.WriteLine("Refused: supply the full HID interface path printed by --diagnose.");
            return 2;
        }

        try
        {
            var devices = _enumerate();
            if (list)
            {
                _output.WriteLine($"HID collections: {devices.Count} (enumeration only; no reports sent)");
                foreach (var device in devices)
                {
                    PrintDevice(device);
                }

                return 0;
            }

            var matches = devices.Where(device =>
                string.Equals(device.Path, args[1], StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                _output.WriteLine("Refused: the full path must identify exactly one currently enumerated HID collection.");
                return 2;
            }

            var selected = matches[0];
            PrintDevice(selected);
            if (inspect)
            {
                _output.WriteLine("Inspection only; no reports sent.");
                return 0;
            }

            if (!MchoseDevice.FindCandidates(matches).Any())
            {
                _output.WriteLine("Refused: probing requires VID:PID 291D:385D with 64-byte input and output reports.");
                return 2;
            }

            var request = MchoseProtocol.CreateBatteryRequest();
            PrintFrame("Request", request);
            var reply = await _transport.Exchange(selected, request, cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                _output.WriteLine("Reply: <no reply> (timeout or unavailable headset)");
                return 1;
            }

            PrintFrame("Reply", reply);
            if (!MchoseProtocol.TryParseBatteryResponse(reply, out var reading))
            {
                _output.WriteLine("Invalid battery reply; protocol compatibility has not been confirmed.");
                return 1;
            }

            _output.WriteLine($"Battery: {reading.Percentage}%; status: 0x{reading.StatusCode:X2} (raw, not interpreted)");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _output.WriteLine("Diagnostic cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Diagnostic failed: {ex.Message}");
            return 1;
        }
    }

    private void PrintDevice(HidDeviceInfo device)
    {
        _output.WriteLine();
        _output.WriteLine($"Path: {device.Path}");
        _output.WriteLine($"VID:PID: {device.VendorId:X4}:{device.ProductId:X4}; Version: 0x{device.VersionNumber:X4}");
        _output.WriteLine($"Manufacturer: {device.Manufacturer ?? "<unavailable>"}");
        _output.WriteLine($"Product: {device.Product ?? "<unavailable>"}");
        _output.WriteLine($"Serial: {device.SerialNumber ?? "<unavailable>"}");
        _output.WriteLine($"Usage Page: 0x{device.UsagePage:X4}; Usage: 0x{device.Usage:X4}");
        _output.WriteLine($"Input: {device.InputReportByteLength}; Output: {device.OutputReportByteLength}; Feature: {device.FeatureReportByteLength}");
    }

    private void PrintFrame(string label, byte[] frame) =>
        _output.WriteLine($"{label} ({frame.Length} bytes): {string.Join(" ", frame.Select(value => value.ToString("X2")))}");
}
