using System.Runtime.InteropServices;
using MchoseBattery.Core.Diagnostics;
using MchoseBattery.Core.Services;

namespace MchoseBattery.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BatteryService _service;
    private readonly IAppLogger _logger;
    private readonly string _executablePath;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _startup;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly DeviceNotificationWindow _notificationWindow;
    private bool _disposed;

    public TrayApplicationContext(BatteryService service, IAppLogger logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
        _status = new ToolStripMenuItem { Enabled = false };
        var refresh = new ToolStripMenuItem("Atualizar agora", null, (_, _) => _service.RequestRefresh());
        _startup = new ToolStripMenuItem("Abrir ao iniciar o Windows", null, (_, _) => ToggleStartup());
        var exit = new ToolStripMenuItem("Sair", null, (_, _) => ExitThread());
        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(new ToolStripItem[] { _status, new ToolStripSeparator(), refresh, _startup, exit });
        _icon = (Icon)SystemIcons.Information.Clone();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = _menu,
        };

        // This is a hidden top-level NativeWindow, never a Form or a message-only
        // window: it must receive broadcast device-tree changes as a fallback.
        _notificationWindow = new DeviceNotificationWindow(
            _service.RequestRefresh, ApplyCurrentSnapshot, _logger);
        InitializeStartupState();
        _service.SnapshotChanged += OnSnapshotChanged;
        ApplyCurrentSnapshot();
        _notifyIcon.Visible = true;
        _service.StartPolling();
        _service.RequestRefresh();
    }

    public static string GetStatusText(BatterySnapshot snapshot) => snapshot.State switch
    {
        BatteryConnectionState.Connected when snapshot.Percentage is >= 0 and <= 100 =>
            $"MCHOSE V9 Pro: {snapshot.Percentage}%",
        BatteryConnectionState.DongleNotFound => "Dongle não encontrado",
        _ => "Headset desconectado",
    };

    private void OnSnapshotChanged(object? sender, BatterySnapshot snapshot)
    {
        // BatteryService raises this event on a worker thread. The window posts a
        // message so all NotifyIcon and ToolStrip changes run on the UI thread.
        _notificationWindow.PostSnapshotChanged();
    }

    private void ApplyCurrentSnapshot()
    {
        if (_disposed)
        {
            return;
        }

        var text = GetStatusText(_service.CurrentSnapshot);
        _status.Text = text;
        _notifyIcon.Text = text;
    }

    private void InitializeStartupState()
    {
        try
        {
            _startup.Checked = StartupRegistration.IsEnabled(_executablePath);
        }
        catch (Exception ex)
        {
            _startup.Enabled = false;
            _logger.Write($"Cannot read Windows startup registration ({ex.GetType().Name}).");
        }
    }

    private void ToggleStartup()
    {
        var enable = !_startup.Checked;
        if (StartupRegistration.TrySetEnabled(enable, _executablePath, out var error))
        {
            _startup.Checked = enable;
            return;
        }

        _logger.Write($"Cannot update Windows startup registration: {error}");
        _notifyIcon.ShowBalloonTip(3000, "MCHOSE Battery",
            "Não foi possível alterar a inicialização com o Windows.", ToolTipIcon.Warning);
    }

    protected override void ExitThreadCore()
    {
        Dispose();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _service.SnapshotChanged -= OnSnapshotChanged;
            _service.Dispose();
            _notificationWindow.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class DeviceNotificationWindow : NativeWindow, IDisposable
    {
        private const int DeviceChangeMessage = 0x0219;
        private const int SnapshotChangedMessage = 0x8001; // WM_APP + 1
        private readonly object _handleGate = new();
        private readonly Action _deviceChanged;
        private readonly Action _snapshotChanged;
        private readonly IAppLogger _logger;
        private IntPtr _registration;
        private bool _disposed;

        public DeviceNotificationWindow(Action deviceChanged, Action snapshotChanged, IAppLogger logger)
        {
            _deviceChanged = deviceChanged;
            _snapshotChanged = snapshotChanged;
            _logger = logger;
            CreateHandle(new CreateParams
            {
                Caption = "MchoseBattery device notifications",
                Style = 0, // No WS_VISIBLE: no displayed window or taskbar entry.
                ExStyle = 0x00000080, // WS_EX_TOOLWINDOW
            });

            HidD_GetHidGuid(out var hidGuid);
            var filter = new DeviceBroadcastInterface
            {
                Size = Marshal.SizeOf<DeviceBroadcastInterface>(),
                DeviceType = 5, // DBT_DEVTYP_DEVICEINTERFACE
                ClassGuid = hidGuid,
            };
            _registration = RegisterDeviceNotification(Handle, ref filter, 0); // DEVICE_NOTIFY_WINDOW_HANDLE
            if (_registration == IntPtr.Zero)
            {
                _logger.Write($"HID notifications unavailable (Win32 {Marshal.GetLastWin32Error()}); using device-tree broadcasts.");
            }
        }

        public void PostSnapshotChanged()
        {
            lock (_handleGate)
            {
                if (!_disposed && !PostMessage(Handle, SnapshotChangedMessage, IntPtr.Zero, IntPtr.Zero))
                {
                    _logger.Write($"Cannot post battery status to tray (Win32 {Marshal.GetLastWin32Error()}).");
                }
            }
        }

        protected override void WndProc(ref Message message)
        {
            try
            {
                if (message.Msg == SnapshotChangedMessage)
                {
                    _snapshotChanged();
                }
                else if (message.Msg == DeviceChangeMessage && (int)message.WParam is 0x0007 or 0x8000 or 0x8004)
                {
                    // DBT_DEVNODES_CHANGED, DBT_DEVICEARRIVAL, DBT_DEVICEREMOVECOMPLETE.
                    _deviceChanged();
                }
            }
            catch (Exception ex)
            {
                _logger.Write($"Device notification callback failed ({ex.GetType().Name}).");
            }

            base.WndProc(ref message);
        }

        public void Dispose()
        {
            lock (_handleGate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_registration != IntPtr.Zero)
                {
                    UnregisterDeviceNotification(_registration);
                    _registration = IntPtr.Zero;
                }

                DestroyHandle();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceBroadcastInterface
        {
            public int Size;
            public int DeviceType;
            public int Reserved;
            public Guid ClassGuid;
            public short Name;
        }

        [DllImport("hid.dll", ExactSpelling = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("user32.dll", EntryPoint = "RegisterDeviceNotificationW", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr RegisterDeviceNotification(
            IntPtr recipient, ref DeviceBroadcastInterface notificationFilter, uint flags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterDeviceNotification(IntPtr handle);

        [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
