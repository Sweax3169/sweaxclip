using System.IO;
using System.Windows.Interop;
using System.Windows.Threading;
using SweaxClip.Native;

namespace SweaxClip.Core;

/// <summary>
/// One invisible top-level window that owns the clipboard listener, the tray icon and session
/// notifications. Event-driven: when nothing is copied, SweaxClip does literally nothing.
/// </summary>
public sealed class HostWindow : IDisposable
{
    public const int WM_TRAY = Win32.WM_APP + 1;
    readonly HwndSource _src;
    readonly uint _taskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");
    readonly DispatcherTimer _debounce;
    int _pendingUpdates;

    public IntPtr Handle => _src.Handle;

    public event Action? ClipboardChanged;
    public event Action<int>? TrayMessage;
    public event Action? TaskbarCreated;
    public event Action? SessionUnlocked;

    public HostWindow()
    {
        var p = new HwndSourceParameters("SweaxClip.Host")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ExtendedWindowStyle = (int)Win32.WS_EX_TOOLWINDOW,
        };
        _src = new HwndSource(p);
        _src.AddHook(WndProc);
        Win32.AddClipboardFormatListener(Handle);
        Win32.WTSRegisterSessionNotification(Handle, 0);

        // Some apps set the clipboard several times in a row; read once it settles.
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(90) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _pendingUpdates = 0;
            ClipboardChanged?.Invoke();
        };
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_CLIPBOARDUPDATE:
                _pendingUpdates++;
                _debounce.Stop();
                _debounce.Start();
                handled = true;
                break;
            case WM_TRAY:
                TrayMessage?.Invoke((int)((long)lParam & 0xFFFF));
                handled = true;
                break;
            case Win32.WM_WTSSESSION_CHANGE:
                if ((int)wParam is 0x8 or 0x2) SessionUnlocked?.Invoke(); // unlock / console connect
                break;
            default:
                if (msg == _taskbarCreated) TaskbarCreated?.Invoke();
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Win32.RemoveClipboardFormatListener(Handle);
        Win32.WTSUnRegisterSessionNotification(Handle);
        _src.Dispose();
    }
}

/// <summary>Notification-area icon drawn with plain Shell_NotifyIcon — no WinForms dependency.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly HostWindow _host;
    IntPtr _icon;
    bool _added;
    string _tip;

    public event Action? LeftClick;
    public event Action? RightClick;

    public TrayIcon(HostWindow host, string tip)
    {
        _host = host;
        _tip = tip;
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SweaxClip.exe");
        var small = new IntPtr[1];
        if (Win32.ExtractIconEx(exe, 0, null, small, 1) > 0) _icon = small[0];
        host.TrayMessage += OnMessage;
        host.TaskbarCreated += () => { _added = false; Add(); };
        Add();
    }

    void OnMessage(int msg)
    {
        switch (msg)
        {
            // NOTIFYICON_VERSION_4 semantics: select = left click / Enter, context menu = right click / Shift+F10
            case Win32.NIN_SELECT or Win32.NIN_KEYSELECT: LeftClick?.Invoke(); break;
            case Win32.WM_CONTEXTMENU: RightClick?.Invoke(); break;
        }
    }

    Win32.NOTIFYICONDATA Data() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
        hWnd = _host.Handle,
        uID = 1,
        uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP,
        uCallbackMessage = HostWindow.WM_TRAY,
        hIcon = _icon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
        uVersion = Win32.NOTIFYICON_VERSION_4,
    };

    void Add()
    {
        if (_added) return;
        var d = Data();
        _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref d);
        if (_added) Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref d);
    }

    public void SetTip(string tip)
    {
        _tip = tip;
        var d = Data();
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref d);
    }

    public void Dispose()
    {
        var d = Data();
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref d);
        if (_icon != IntPtr.Zero) Win32.DestroyIcon(_icon);
    }
}
