using System.Runtime.InteropServices;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// Finds the text cursor of another app in screen pixels. Classic Win32 edit controls report it through
/// GetGUIThreadInfo; Chrome, Edge, Firefox, Office and Electron apps only expose it through the
/// accessibility caret (OBJID_CARET), which screen magnifiers use.
/// </summary>
internal static class CaretLocator
{
    const uint OBJID_CARET = 0xFFFFFFF8;

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? obj);

    public static Win32.RECT? Find(IntPtr window)
    {
        if (window == IntPtr.Zero || !Win32.IsWindow(window)) return null;
        // MSAA calls go into the other process; never let a hung app stall the panel.
        var task = Task.Run(() => FindCore(window));
        return task.Wait(150) ? task.Result : null;
    }

    static Win32.RECT? FindCore(IntPtr window)
    {
        Win32.GetWindowRect(window, out var bounds);
        uint thread = Win32.GetWindowThreadProcessId(window, out _);
        var gui = new Win32.GUITHREADINFO { cbSize = Marshal.SizeOf<Win32.GUITHREADINFO>() };
        IntPtr focus = window;
        if (thread != 0 && Win32.GetGUIThreadInfo(thread, ref gui))
        {
            if (gui.hwndFocus != IntPtr.Zero) focus = gui.hwndFocus;
            if (gui.hwndCaret != IntPtr.Zero && (gui.rcCaret.Bottom > gui.rcCaret.Top))
            {
                var tl = new Win32.POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Top };
                var br = new Win32.POINT { X = gui.rcCaret.Right, Y = gui.rcCaret.Bottom };
                Win32.ClientToScreen(gui.hwndCaret, ref tl);
                Win32.ClientToScreen(gui.hwndCaret, ref br);
                var r = new Win32.RECT { Left = tl.X, Top = tl.Y, Right = Math.Max(br.X, tl.X + 1), Bottom = br.Y };
                if (Inside(r, bounds)) return r;
            }
        }
        return FromAccessibility(focus, bounds) ?? (focus != window ? FromAccessibility(window, bounds) : null);
    }

    static Win32.RECT? FromAccessibility(IntPtr hwnd, Win32.RECT bounds)
    {
        try
        {
            var iid = typeof(Accessibility.IAccessible).GUID;
            if (AccessibleObjectFromWindow(hwnd, OBJID_CARET, ref iid, out var obj) != 0 || obj is not Accessibility.IAccessible acc) return null;
            acc.accLocation(out int l, out int t, out int w, out int h, 0);
            Marshal.ReleaseComObject(acc);
            if (l == 0 && t == 0 && w == 0 && h == 0) return null;
            var r = new Win32.RECT { Left = l, Top = t, Right = l + Math.Max(1, w), Bottom = t + Math.Max(h, 14) };
            return Inside(r, bounds) ? r : null;
        }
        catch { return null; }
    }

    static bool Inside(Win32.RECT r, Win32.RECT window) =>
        r.Left >= window.Left - 4 && r.Left <= window.Right + 4 && r.Top >= window.Top - 4 && r.Bottom <= window.Bottom + 4;
}
