using System.Runtime.InteropServices;
using SweaxClip.Native;

namespace SweaxClip.Core;

/// <summary>A global shortcut such as Win+V. Parsed from "Win+V", "Ctrl+Shift+V", …</summary>
public readonly record struct Hotkey(bool Win, bool Ctrl, bool Alt, bool Shift, int Key)
{
    public static Hotkey Parse(string? s)
    {
        bool win = false, ctrl = false, alt = false, shift = false;
        int key = Win32.VK_V;
        foreach (var part in (s ?? "Win+V").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "win": win = true; break;
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default:
                    if (part.Length == 1 && char.IsLetterOrDigit(part[0])) key = char.ToUpperInvariant(part[0]);
                    else if (part.StartsWith('F') && int.TryParse(part[1..], out int f) && f is >= 1 and <= 24) key = 0x6F + f;
                    else if (Named.FirstOrDefault(n => n.Value.Equals(part, StringComparison.OrdinalIgnoreCase)) is { Value: not null } hit) key = hit.Key;
                    break;
            }
        }
        if (!win && !ctrl && !alt && !IsFree(key)) win = true; // never steal a bare key that is used for typing
        return new Hotkey(win, ctrl, alt, shift, key);
    }

    static readonly Dictionary<int, string> Named = new()
    {
        [0x20] = "Space", [0x2D] = "Insert", [0x2C] = "PrtSc", [0x24] = "Home", [0x23] = "End", [0x21] = "PgUp", [0x22] = "PgDn",
        [0x13] = "Pause", [0x91] = "ScrollLock", [0xB6] = "App1", [0xB7] = "App2",
    };

    /// <summary>Keys a shortcut can end on: letters, digits, F1–F24 and a few named keys.</summary>
    public static bool IsBindable(int vk) => vk is (>= 0x41 and <= 0x5A) or (>= 0x30 and <= 0x39) or (>= 0x70 and <= 0x87) || Named.ContainsKey(vk);

    /// <summary>Keys nobody types with (F13–F24, Pause, Scroll Lock, the launch keys): a shortcut on its own, which suits macro keys.</summary>
    public static bool IsFree(int vk) => vk is (>= 0x7C and <= 0x87) or 0x13 or 0x91 or 0xB6 or 0xB7;

    public bool HasModifier => Win || Ctrl || Alt;

    /// <summary>The form saved in settings: "Win+Shift+T".</summary>
    public string Spec => ToString().Replace(" ", "");

    public override string ToString()
    {
        var parts = new List<string>();
        if (Win) parts.Add("Win");
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key is >= 0x70 and <= 0x87 ? "F" + (Key - 0x6F) : Named.TryGetValue(Key, out var n) ? n : ((char)Key).ToString());
        return string.Join(" + ", parts);
    }
}

/// <summary>A key pressed or released while the panel is open. <see cref="Alt"/> includes the Alt that SweaxClip itself swallowed.</summary>
public readonly record struct CapturedKey(int Vk, int Scan, bool Down, bool Repeat, bool Ctrl, bool Shift, bool Alt);

/// <summary>
/// Low-level keyboard hook on its own thread with its own message loop, so a busy UI can never
/// make Windows drop the hook. It swallows the shortcut (Win+V never reaches the shell) and
/// taps an unassigned key so the Start menu does not open when Win is released.
/// While the panel is open it also takes the keys meant for the panel, because the panel never takes focus:
/// the app below keeps its text cursor, and the paste lands exactly where the user was typing.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    const uint WM_REHOOK = Win32.WM_APP + 7, WM_MOUSEHOOK = Win32.WM_APP + 8;

    readonly Win32.HookProc _proc, _mouseProc;
    readonly Thread _thread;
    uint _threadId;
    IntPtr _hook, _mouseHook;
    volatile int _swallowKey;
    volatile Action<Hotkey?>? _record;
    readonly ManualResetEventSlim _ready = new();
    // keys whose press went to the panel: their repeats and release go there too, so the app never sees half a keystroke
    readonly bool[] _eaten = new bool[256];
    volatile bool _capture;
    long _waitingSince;
    // how late Windows handed the hook a key (the PC was too busy for it): noted in the log every 20 s at most
    long _worstLate, _lateKeys;

    public volatile bool Enabled = true;
    public Hotkey Hotkey { get; set; } = Hotkey.Parse("Win+V");
    /// <summary>"Copy text from screen"; null when it is turned off.</summary>
    public Hotkey? GrabHotkey { get; set; }
    public long LastPrintScreen;

    /// <summary>Raised on the hook thread — handlers must only post work elsewhere.</summary>
    public event Action? Pressed;
    /// <summary>The text-grab shortcut was pressed (hook thread).</summary>
    public event Action? GrabPressed;
    /// <summary>A key for the open panel (hook thread). Call <see cref="Handled"/> once it has been dealt with.</summary>
    public event Action<CapturedKey>? Key;
    /// <summary>A mouse button went down somewhere while the panel was open (hook thread); the click itself goes on.</summary>
    internal event Action<Win32.POINT>? MouseDown;
    /// <summary>The UI stopped answering, so the keyboard went back to the apps (hook thread).</summary>
    public event Action? CaptureLost;

    /// <summary>
    /// While on, the keys the panel uses (letters, digits, arrows, Enter, Esc, Tab, Backspace, Delete, Alt…) go to it
    /// instead of the app under it. Win combinations, function and media keys still reach Windows.
    /// </summary>
    public bool Capture
    {
        get => _capture;
        set
        {
            if (_capture == value) return;
            _capture = value;
            Interlocked.Exchange(ref _waitingSince, 0);
            if (_threadId != 0) Win32.PostThreadMessage(_threadId, WM_MOUSEHOOK, value ? 1 : 0, IntPtr.Zero);
        }
    }

    /// <summary>The UI dealt with the keys sent so far. If it stops answering for a second, keys go back to the app.</summary>
    public void Handled() => Interlocked.Exchange(ref _waitingSince, 0);

    /// <summary>
    /// Captures the next shortcut pressed anywhere (even ones Windows would take, such as Win + letter) and
    /// reports it instead of acting on it; Esc reports null. The callback runs on the hook thread.
    /// </summary>
    public void Record(Action<Hotkey?> done) => _record = done;
    public void StopRecording() => _record = null;

    public KeyboardHook()
    {
        _proc = Callback;
        _mouseProc = MouseCallback;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SweaxClip.Hook", Priority = ThreadPriority.Highest };
        _thread.Start();
        _ready.Wait(2000);
    }

    void Loop()
    {
        _threadId = Win32.GetCurrentThreadId();
        Install();
        // Windows silently drops a hook that ever answers too slowly, and hooks installed later by other apps run
        // before ours; installing again every 20 s puts SweaxClip back at the front of the chain.
        Win32.SetTimer(IntPtr.Zero, UIntPtr.Zero, 20_000, IntPtr.Zero);
        _ready.Set();
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.hwnd == IntPtr.Zero && msg.message is WM_REHOOK or Win32.WM_TIMER)
            {
                Uninstall();
                Install();
                long worst = Interlocked.Exchange(ref _worstLate, 0), late = Interlocked.Exchange(ref _lateKeys, 0);
                if (late > 0) Paths.Log($"{late} key(s) reached SweaxClip's keyboard hook late, the worst {worst} ms: the PC was too busy (Windows drops a hook that answers too late; it was put back)");
                continue;
            }
            if (msg.message == WM_MOUSEHOOK)
            {
                if (msg.wParam != IntPtr.Zero && _mouseHook == IntPtr.Zero) _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, Win32.GetModuleHandle(null), 0);
                else if (msg.wParam == IntPtr.Zero && _mouseHook != IntPtr.Zero) { Win32.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
                continue;
            }
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        Uninstall();
        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
    }

    void Install()
    {
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Paths.Log("keyboard hook failed: " + Marshal.GetLastWin32Error());
    }

    void Uninstall()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>Re-installs the hook now (after the session is unlocked, and as a safety net).</summary>
    public void Rearm() => Win32.PostThreadMessage(_threadId, WM_REHOOK, IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// Every key typed anywhere passes through here, so it must answer in microseconds: it reads the event in place,
    /// allocates nothing on the common path and only posts work to the UI.
    /// </summary>
    unsafe IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb = (Win32.KBDLLHOOKSTRUCT*)lParam;
            int msg = (int)wParam;
            uint late = unchecked((uint)Environment.TickCount - kb->time);
            if (late is > 400 and < 60_000)
            {
                Interlocked.Increment(ref _lateKeys);
                if (late > Interlocked.Read(ref _worstLate)) Interlocked.Exchange(ref _worstLate, late);
            }
            bool down = msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
            bool up = msg is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
            // keys we send ourselves (Ctrl+V, the Start-menu blocker) are skipped; keys injected by macro software
            // and keyboard drivers are welcome, so a macro key can press the shortcut
            bool ours = (kb->flags & Win32.LLKHF_INJECTED) != 0 && kb->dwExtraInfo == Win32.SweaxMark;

            if (!ours)
            {
                int vk = (int)kb->vkCode & 0xFF;
                if (vk == Win32.VK_SNAPSHOT && down) LastPrintScreen = Environment.TickCount64;

                if (down && _record is { } record)
                {
                    if (vk == 0x1B) { _record = null; _swallowKey = vk; record(null); return 1; }
                    if (Hotkey.IsBindable(vk))
                    {
                        var got = Current(vk);
                        _record = null;
                        Swallow(got);
                        record(got);
                        return 1;
                    }
                }
                else if (Enabled && down)
                {
                    if (_swallowKey == vk) return 1; // auto-repeat while held
                    var hk = Hotkey;
                    if (vk == hk.Key && ModifiersMatch(hk)) { Swallow(hk); Pressed?.Invoke(); return 1; }
                    if (GrabHotkey is { } gk && vk == gk.Key && ModifiersMatch(gk)) { Swallow(gk); GrabPressed?.Invoke(); return 1; }
                }
                if (up && _swallowKey == vk)
                {
                    _swallowKey = 0;
                    return 1;
                }

                if (_capture && Stalled()) { _capture = false; CaptureLost?.Invoke(); }
                // Alt+Tab, Alt+F4 and the like belong to Windows: hand it the Alt the panel was holding, in the right order
                if (down && _capture && !_eaten[vk] && (_eaten[Win32.VK_LMENU] || _eaten[Win32.VK_RMENU]) && (vk == 0x09 || !ForPanel(vk) && !IsModifier(vk)))
                {
                    int alt = _eaten[Win32.VK_LMENU] ? Win32.VK_LMENU : Win32.VK_RMENU;
                    _eaten[Win32.VK_LMENU] = _eaten[Win32.VK_RMENU] = false;
                    Post(new CapturedKey(alt, 0x38, false, false, false, false, false));
                    Win32.Send(Win32.Key(alt, false), Win32.Key(vk, false));
                    return 1;
                }
                // a key held since the panel took it keeps going to the panel, even after it closed (no stray Enter repeats)
                if (down && (_eaten[vk] || _capture && ForPanel(vk) && !WinDown()))
                {
                    bool repeat = _eaten[vk];
                    _eaten[vk] = true;
                    if (_capture) Post(new CapturedKey(vk, (int)kb->scanCode, true, repeat, Win32.IsKeyDown(Win32.VK_CONTROL), Win32.IsKeyDown(Win32.VK_SHIFT), AltDown()));
                    return 1;
                }
                if (up && _eaten[vk])
                {
                    _eaten[vk] = false;
                    if (_capture) Post(new CapturedKey(vk, (int)kb->scanCode, false, false, Win32.IsKeyDown(Win32.VK_CONTROL), Win32.IsKeyDown(Win32.VK_SHIFT), AltDown()));
                    return 1;
                }
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    unsafe IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _capture && (int)wParam is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or Win32.WM_XBUTTONDOWN)
            MouseDown?.Invoke(((Win32.MSLLHOOKSTRUCT*)lParam)->pt);
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    void Post(CapturedKey key)
    {
        Interlocked.CompareExchange(ref _waitingSince, Environment.TickCount64, 0);
        Key?.Invoke(key);
    }

    /// <summary>The UI has not taken the keys sent over a second ago: it is stuck, so give the keyboard back.</summary>
    bool Stalled()
    {
        long since = Interlocked.Read(ref _waitingSince);
        return since != 0 && Environment.TickCount64 - since > 1000;
    }

    /// <summary>Keys the panel uses: typing, editing and moving. Function, media, lock and Win keys stay with Windows.</summary>
    static bool ForPanel(int vk) => vk is 0x08 or 0x09 or 0x0D or 0x1B or 0x20 or (>= 0x21 and <= 0x28) or 0x2E
        or (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or (>= 0x60 and <= 0x6F) or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2
        or Win32.VK_LMENU or Win32.VK_RMENU;

    static bool IsModifier(int vk) => vk is (>= 0x10 and <= 0x12) or 0x14 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5) or 0x90 or 0x91;

    static bool WinDown() => Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN);

    bool AltDown() => _eaten[Win32.VK_LMENU] || _eaten[Win32.VK_RMENU] || Win32.IsKeyDown(Win32.VK_MENU);

    /// <summary>Eats the key's release too, and taps an unused key so a held Win or Alt doesn't open Start or the menu bar.</summary>
    void Swallow(Hotkey hk)
    {
        _swallowKey = hk.Key;
        if (hk.Win || hk.Alt) Win32.Send(Win32.Key(Win32.VK_MASK, false), Win32.Key(Win32.VK_MASK, true));
    }

    static Hotkey Current(int vk) => new(
        Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN), Win32.IsKeyDown(Win32.VK_CONTROL),
        Win32.IsKeyDown(Win32.VK_MENU), Win32.IsKeyDown(Win32.VK_SHIFT), vk);

    static bool ModifiersMatch(Hotkey hk)
    {
        bool win = Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN);
        bool ctrl = Win32.IsKeyDown(Win32.VK_CONTROL);
        bool alt = Win32.IsKeyDown(Win32.VK_MENU);
        bool shift = Win32.IsKeyDown(Win32.VK_SHIFT);
        return win == hk.Win && ctrl == hk.Ctrl && alt == hk.Alt && shift == hk.Shift;
    }

    public void Dispose()
    {
        if (_threadId != 0) Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }
}
