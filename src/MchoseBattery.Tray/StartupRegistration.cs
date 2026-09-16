using System.Security;
using Microsoft.Win32;

namespace MchoseBattery.Tray;

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MchoseBattery";

    public static string BuildCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) ||
            executablePath.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0 ||
            !Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("A full executable path without quotes or control characters is required.",
                nameof(executablePath));
        }

        return $"\"{executablePath}\"";
    }

    public static bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return string.Equals(key?.GetValue(ValueName) as string, BuildCommand(executablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool TrySetEnabled(bool enabled, string executablePath, out string? error)
    {
        try
        {
            var command = BuildCommand(executablePath);
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }
}
