using System.Text;

namespace MchoseBattery.Core.Diagnostics;

public interface IAppLogger
{
    void Write(string message);
}

public sealed class AppLogger : IAppLogger
{
    private const int MaxBytes = 256 * 1024;
    private readonly object _gate = new();
    private readonly string _path;

    public AppLogger(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MchoseBattery",
            "mchose-battery.log");
    }

    public void Write(string message)
    {
        // Diagnostics must never interrupt battery polling or the tray loop.
        try
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var clean = message.Replace('\r', ' ').Replace('\n', ' ');
                if (clean.Length > 200)
                {
                    clean = clean[..200];
                }

                var line = Encoding.UTF8.GetBytes($"{DateTimeOffset.UtcNow:O} {clean}\n");
                using var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                stream.Seek(0, SeekOrigin.End);
                stream.Write(line);
                if (stream.Length <= MaxBytes)
                {
                    return;
                }

                stream.Seek(-MaxBytes, SeekOrigin.End);
                var retained = new byte[MaxBytes];
                stream.ReadExactly(retained);
                var firstNewline = Array.IndexOf(retained, (byte)'\n');
                var start = firstNewline >= 0 ? firstNewline + 1 : 0;
                stream.SetLength(0);
                stream.Position = 0;
                stream.Write(retained.AsSpan(start));
            }
        }
        catch
        {
            // Logging is best-effort by design.
        }
    }
}
