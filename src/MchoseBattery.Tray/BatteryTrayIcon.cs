using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using MchoseBattery.Core.Services;

namespace MchoseBattery.Tray;

/// <summary>
/// Draws the battery state directly into the tray icon so the charge is readable
/// without hovering. The icon is a filled rounded square so it keeps its contrast
/// on both the light and the dark taskbar.
/// </summary>
public static class BatteryTrayIcon
{
    private const int MinimumEdge = 16;
    private const int MaximumEdge = 64;
    private static readonly Color LowCharge = Color.FromArgb(0xD1, 0x34, 0x38);
    private static readonly Color MediumCharge = Color.FromArgb(0xFF, 0xB9, 0x00);
    private static readonly Color HighCharge = Color.FromArgb(0x10, 0x7C, 0x10);
    private static readonly Color Unavailable = Color.FromArgb(0x6E, 0x6E, 0x6E);

    public static string GetIconText(BatterySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.State switch
        {
            BatteryConnectionState.Connected when snapshot.Percentage is >= 0 and <= 100 =>
                snapshot.Percentage.Value.ToString(CultureInfo.InvariantCulture),
            BatteryConnectionState.DongleNotFound => "?",
            _ => "--",
        };
    }

    public static Color GetBackgroundColor(BatterySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.State switch
        {
            BatteryConnectionState.Connected when snapshot.Percentage is >= 0 and <= 20 => LowCharge,
            BatteryConnectionState.Connected when snapshot.Percentage is > 20 and <= 40 => MediumCharge,
            BatteryConnectionState.Connected when snapshot.Percentage is > 40 and <= 100 => HighCharge,
            _ => Unavailable,
        };
    }

    public static Color GetForegroundColor(Color background) =>
        (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B) >= 140.0
            ? Color.Black
            : Color.White;

    /// <summary>
    /// Renders one icon. The caller owns the result and must dispose it after the
    /// tray has stopped using it.
    /// </summary>
    public static Icon Create(BatterySnapshot snapshot, int size)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var edge = Math.Clamp(size, MinimumEdge, MaximumEdge);
        var background = GetBackgroundColor(snapshot);

        using var bitmap = new Bitmap(edge, edge, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            using (var brush = new SolidBrush(background))
            using (var shape = CreateRoundedSquare(edge))
            {
                graphics.FillPath(brush, shape);
            }

            DrawFittedText(graphics, GetIconText(snapshot), GetForegroundColor(background), edge);
        }

        return FromBitmap(bitmap);
    }

    private static GraphicsPath CreateRoundedSquare(int edge)
    {
        var bounds = new Rectangle(0, 0, edge - 1, edge - 1);
        var diameter = Math.Max(2, edge / 4);
        var path = new GraphicsPath();
        try
        {
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
        catch
        {
            path.Dispose();
            throw;
        }
    }

    private static void DrawFittedText(Graphics graphics, string text, Color color, int edge)
    {
        // Two digits fill the icon; "100" has to shrink. Measure instead of guessing
        // so the icon stays legible at every DPI-dependent tray size.
        var available = edge * 0.86f;
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        using var brush = new SolidBrush(color);
        var box = new RectangleF(0, 0, edge, edge);

        for (var pixels = edge * 0.84f; pixels >= 4f; pixels -= 0.5f)
        {
            using var font = new Font("Segoe UI", pixels, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(text, font, PointF.Empty, format);
            if (measured.Width <= available && measured.Height <= available)
            {
                graphics.DrawString(text, font, brush, box, format);
                return;
            }
        }

        using var smallest = new Font("Segoe UI", 4f, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawString(text, smallest, brush, box, format);
    }

    private static Icon FromBitmap(Bitmap bitmap)
    {
        // GetHicon hands out an unmanaged icon that Icon.FromHandle does not own.
        // Clone into a managed icon and destroy the handle, or the tray leaks a GDI
        // object on every refresh.
        var handle = bitmap.GetHicon();
        try
        {
            using var unmanaged = Icon.FromHandle(handle);
            return (Icon)unmanaged.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
