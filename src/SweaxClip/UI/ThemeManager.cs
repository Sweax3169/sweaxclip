using System.Windows;
using Microsoft.Win32;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// Builds the token dictionary from the chosen palette and mode (light, dark, or following Windows)
/// and swaps it live, so every window recolours at once.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static bool? Forced { get; set; }
    /// <summary>"system", "light" or "dark".</summary>
    public static string Mode { get; set; } = "system";
    public static string PaletteKey { get; set; } = "ember";
    public static event Action? Changed;

    public static void Apply()
    {
        IsDark = Forced ?? Mode switch { "dark" => true, "light" => false, _ => ReadDark() };
        var app = Application.Current;
        if (app == null) return;
        var dict = Palettes.Build(Palettes.Get(PaletteKey), IsDark);
        var md = app.Resources.MergedDictionaries;
        foreach (var old in md.Where(d => d.Contains("SweaxTheme") || d.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true).ToList())
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
