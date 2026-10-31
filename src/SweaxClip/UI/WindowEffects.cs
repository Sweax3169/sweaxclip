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
}
