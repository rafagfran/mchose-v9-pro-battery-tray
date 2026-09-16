using System.Runtime.InteropServices;
using MchoseBattery.Core.Devices;
using MchoseBattery.Core.Diagnostics;
using MchoseBattery.Core.Services;

namespace MchoseBattery.Tray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            PrepareDiagnosticOutput();
            var diagnostics = new DiagnosticsCommand(
                () => new HidDeviceEnumerator().Enumerate(), new WindowsHidTransport(), Console.Out);
            return diagnostics.RunAsync(args).GetAwaiter().GetResult();
        }

        var logger = new AppLogger();
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, eventArgs) =>
                logger.Write($"Tray callback failed ({eventArgs.Exception.GetType().Name}).");

            using var service = new BatteryService(
                () => new HidDeviceEnumerator().Enumerate(), new WindowsHidTransport(), logger, startPolling: false);
            using var context = new TrayApplicationContext(service, logger);
            Application.Run(context);
            return 0;
        }
        catch (Exception ex)
        {
            logger.Write($"Tray startup failed ({ex.GetType().Name}): {ex.Message}");
            return 1;
        }
    }

    private static void PrepareDiagnosticOutput()
    {
        // WinExe prevents a console in normal tray mode. Diagnostic invocations reuse
        // redirected stdout, or attach to the invoking terminal when no handle exists.
        var output = GetStdHandle(-11); // STD_OUTPUT_HANDLE
        if (output == IntPtr.Zero || output == new IntPtr(-1))
        {
            AttachConsole(uint.MaxValue); // ATTACH_PARENT_PROCESS
        }

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);
}
