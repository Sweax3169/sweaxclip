using System.Windows.Interop;
using System.Windows.Media;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>Native Windows 11 frame touches: dark frame, ink-coloured border, corner style.</summary>
public static class WindowEffects
{
    public static void Apply(IntPtr hwnd, bool small)
    {
        int on = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int corner = small ? 3 : 2; // DWMWCP_ROUNDSMALL : DWMWCP_ROUND
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int border = 0x00302B2B; // COLORREF 0x00BBGGRR → #2B2B30
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        int caption = 0x000B0A0A; // #0A0A0B
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
    }

    /// <summary>
    /// See-through without a layered window: the desktop compositor blends WPF's own transparent pixels. A layered window
    /// (AllowsTransparency) has every frame copied back from the graphics card and handed over again, which at a high
    /// refresh rate, or with the card busy with games and live wallpapers, slowed the panel to a crawl.
    /// </summary>
    public static void SeeThrough(IntPtr hwnd)
    {
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
        var all = new Win32.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Win32.DwmExtendFrameIntoClientArea(hwnd, ref all);
        int square = 1; // DWMWCP_DONOTROUND: the panel draws its own corners and shadow
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));
        int none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_BORDER_COLOR, ref none, sizeof(int));
    }
}
