using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// Follows the Windows light/dark setting and the user's accent colour, and applies the
/// matching Fluent token dictionary. Windows notify us through WM_SETTINGCHANGE.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;
    public static Color Accent { get; private set; } = Color.FromRgb(0x00, 0x78, 0xD4);
    public static event Action? Changed;

    static ResourceDictionary? _colors;

    public static bool? Forced { get; set; }

    public static void Apply()
    {
        IsDark = Forced ?? ReadDark();
        Accent = ReadAccent(IsDark);
        var app = Application.Current;
        if (app == null) return;

        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/SweaxClip;component/UI/Themes/{(IsDark ? "Dark" : "Light")}.xaml") };
        var md = app.Resources.MergedDictionaries;
        // drop every theme dictionary, including the design-time default merged in App.xaml
        foreach (var old in md.Where(d => d == _colors || d.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true).ToList())
            md.Remove(old);
        md.Insert(0, dict);
        _colors = dict;

        SetAccent(app.Resources);
        Changed?.Invoke();
    }

    static void SetAccent(ResourceDictionary r)
    {
        // Fluent: dark theme uses the lighter accent shades, light theme the darker ones.
        var fill = IsDark ? Shade(Accent, 0.42) : Shade(Accent, -0.18);
        var fillHover = IsDark ? Shade(fill, -0.08) : Shade(fill, 0.10);
        var text = IsDark ? Shade(Accent, 0.55) : Shade(Accent, -0.30);
        var onAccent = Luma(fill) > 0.55 ? Colors.Black : Colors.White;

        r["AccentColor"] = fill;
        r["AccentFill"] = Freeze(new SolidColorBrush(fill));
        r["AccentFillHover"] = Freeze(new SolidColorBrush(fillHover));
        r["AccentFillPressed"] = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, fill.R, fill.G, fill.B)));
        r["AccentText"] = Freeze(new SolidColorBrush(text));
        r["OnAccent"] = Freeze(new SolidColorBrush(onAccent));
        r["AccentSubtle"] = Freeze(new SolidColorBrush(Color.FromArgb(IsDark ? (byte)0x2E : (byte)0x22, fill.R, fill.G, fill.B)));
        r["AccentStroke"] = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, fill.R, fill.G, fill.B)));
    }

    static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    static bool ReadDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v ? v == 0 : true;
        }
        catch { return true; }
    }

    static Color ReadAccent(bool dark)
    {
        try
        {
            var ui = new Windows.UI.ViewManagement.UISettings();
            var c = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (k?.GetValue("AccentColor") is int abgr)
                    return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
            }
            catch { }
            return Color.FromRgb(0x00, 0x78, 0xD4);
        }
    }

    /// <summary>amount &gt; 0 mixes towards white, &lt; 0 towards black.</summary>
    static Color Shade(Color c, double amount)
    {
        byte M(byte v) => amount >= 0 ? (byte)(v + (255 - v) * amount) : (byte)(v * (1 + amount));
        return Color.FromRgb(M(c.R), M(c.G), M(c.B));
    }

    static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// <summary>Applies DWM frame attributes matching the current theme to a window.</summary>
    public static void ApplyFrame(IntPtr hwnd, Backdrop backdrop)
    {
        int dark = IsDark ? 1 : 0;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int corner = 2; // round
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        if (SupportsBackdrop)
        {
            int type = (int)backdrop;
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
        }
    }

    /// <summary>System backdrops (Mica / Acrylic) exist from Windows 11 22H2 (build 22621).</summary>
    public static bool SupportsBackdrop => Environment.OSVersion.Version.Build >= 22621;
}

public enum Backdrop { None = 1, Mica = 2, Acrylic = 3, MicaAlt = 4 }
