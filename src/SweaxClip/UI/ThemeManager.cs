using System.Windows;
using Microsoft.Win32;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// Picks the light or dark desk to match the Windows app theme and swaps the token dictionary live.
/// The palette itself (paper, ink, vermilion) is SweaxClip's own.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static bool? Forced { get; set; }
    public static event Action? Changed;

    public static void Apply()
    {
        IsDark = Forced ?? ReadDark();
        var app = Application.Current;
        if (app == null) return;
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/SweaxClip;component/UI/Themes/{(IsDark ? "Dark" : "Light")}.xaml") };
        var md = app.Resources.MergedDictionaries;
        foreach (var old in md.Where(d => d.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true).ToList())
            md.Remove(old);
        md.Insert(0, dict);
        Changed?.Invoke();
    }

    static bool ReadDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Dark title-bar hint and rounded corners from DWM; everything inside is drawn by SweaxClip.</summary>
    public static void ApplyFrame(IntPtr hwnd)
    {
        int dark = IsDark ? 1 : 0;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int corner = 2;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int none = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
    }
}
