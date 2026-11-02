using System.Diagnostics;
using System.Text;
using System.Windows;
using SweaxClip.Core;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// Where other windows sit relative to the panel. Windows keeps its own surfaces — Start, Search, Task View, the
/// notification centre, the snipping overlay — in z-order bands above every app window, where no app can draw.
/// </summary>
internal static class Layers
{
    /// <summary>The window's z-order band: 1 for ordinary windows (the panel's), higher for Windows' own surfaces.</summary>
    public static uint Band(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 1;
        try { return Win32.GetWindowBand(hwnd, out uint band) ? band : 1; }
        catch (EntryPointNotFoundException) { return 1; }
    }

    public static bool IsOurs(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>"explorer / XamlExplorerHostIslandWindow / band 3" — process, window class and band, for the log (never titles).</summary>
    public static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "nothing";
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        string name = "?";
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch { }
        var cls = new StringBuilder(128);
        Win32.GetClassName(hwnd, cls, cls.Capacity);
        return $"{name} / {cls} / band {Band(hwnd)}";
    }

    /// <summary>
    /// The window of another app drawn over <paramref name="part"/> of the panel, or zero. A few points are probed
    /// (the middle, near the top, near the left edge) so a small always-on-top window is found too.
    /// </summary>
    public static IntPtr CoveredBy(IntPtr panel, FrameworkElement part)
    {
        if (!part.IsVisible || part.ActualWidth < 60 || part.ActualHeight < 60) return IntPtr.Zero;
        foreach (var p in new[] { new Point(part.ActualWidth / 2, part.ActualHeight / 2), new Point(part.ActualWidth / 2, 24), new Point(24, part.ActualHeight / 2) })
        {
            var s = part.PointToScreen(p);
            IntPtr hit = Win32.GetAncestor(Win32.WindowFromPoint(new Win32.POINT { X = (int)s.X, Y = (int)s.Y }), Win32.GA_ROOT);
            if (hit != IntPtr.Zero && hit != panel && !IsOurs(hit)) return hit;
        }
        return IntPtr.Zero;
    }

    static bool? _meElevated;

    /// <summary>
    /// Whether the window belongs to an administrator (elevated) process while SweaxClip is not one. Windows keeps the
    /// keys typed into such a window from ordinary apps, so Win+V there reaches Windows' own panel instead.
    /// </summary>
    public static bool IsAdminAboveUs(IntPtr hwnd)
    {
        _meElevated ??= Elevated((uint)Environment.ProcessId) == true;
        if (_meElevated == true) return false;
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid != 0 && Elevated(pid) != false;
    }

    /// <summary>True or false from the process token; null when the token can't be read (which an elevated process refuses).</summary>
    static bool? Elevated(uint pid)
    {
        IntPtr proc = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (proc == IntPtr.Zero) return null;
        try
        {
            if (!Win32.OpenProcessToken(proc, 0x0008, out IntPtr token)) return null; // TOKEN_QUERY
            try { return Win32.GetTokenInformation(token, 20, out int elevated, sizeof(int), out _) ? elevated != 0 : null; } // TokenElevation
            finally { Win32.CloseHandle(token); }
        }
        finally { Win32.CloseHandle(proc); }
    }
}
