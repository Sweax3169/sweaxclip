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
                    else if (part.Equals("Insert", StringComparison.OrdinalIgnoreCase)) key = 0x2D;
                    else if (part.Equals("Space", StringComparison.OrdinalIgnoreCase)) key = 0x20;
                    break;
            }
        }
        if (!win && !ctrl && !alt) win = true; // never steal a bare key
        return new Hotkey(win, ctrl, alt, shift, key);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Win) parts.Add("Win");
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key switch { >= 0x70 and <= 0x87 => "F" + (Key - 0x6F), 0x2D => "Insert", 0x20 => "Space", _ => ((char)Key).ToString() });
        return string.Join(" + ", parts);
    }
}

/// <summary>
/// Low-level keyboard hook on its own thread with its own message loop, so a busy UI can never
/// make Windows drop the hook. It swallows the shortcut (Win+V never reaches the shell) and
/// taps an unassigned key so the Start menu does not open when Win is released.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    const uint WM_REHOOK = Win32.WM_APP + 7;

    readonly Win32.HookProc _proc;
    readonly Thread _thread;
    uint _threadId;
    IntPtr _hook;
    volatile bool _swallowUp;
    readonly ManualResetEventSlim _ready = new();

    public volatile bool Enabled = true;
    public Hotkey Hotkey { get; set; } = Hotkey.Parse("Win+V");
    public long LastPrintScreen;

    /// <summary>Raised on the hook thread — handlers must only post work elsewhere.</summary>
    public event Action? Pressed;

    public KeyboardHook()
    {
        _proc = Callback;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SweaxClip.Hook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _ready.Wait(2000);
    }

    void Loop()
    {
        _threadId = Win32.GetCurrentThreadId();
        Install();
        _ready.Set();
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_REHOOK) { Uninstall(); Install(); continue; }
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        Uninstall();
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

    /// <summary>Windows silently removes hooks that ever time out; re-arming now and then keeps us alive for years.</summary>
    public void Rearm() => Win32.PostThreadMessage(_threadId, WM_REHOOK, IntPtr.Zero, IntPtr.Zero);

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
            bool up = msg is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
            bool injected = (kb.flags & Win32.LLKHF_INJECTED) != 0;

            if (!injected)
            {
                if (kb.vkCode == Win32.VK_SNAPSHOT && down) LastPrintScreen = Environment.TickCount64;

                var hk = Hotkey;
                if (Enabled && kb.vkCode == hk.Key)
                {
                    if (down)
                    {
                        if (_swallowUp) return 1; // auto-repeat while held
                        if (ModifiersMatch(hk))
                        {
                            _swallowUp = true;
                            if (hk.Win || hk.Alt) Win32.Send(Win32.Key(Win32.VK_MASK, false), Win32.Key(Win32.VK_MASK, true));
                            Pressed?.Invoke();
                            return 1;
                        }
                    }
                    else if (up && _swallowUp)
                    {
                        _swallowUp = false;
                        return 1;
                    }
                }
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

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
