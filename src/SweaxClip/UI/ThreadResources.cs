using System.Windows;

namespace SweaxClip.UI;

/// <summary>
/// Resources for the UI thread that runs the Win+V panel. WPF objects belong to the thread that made them, so the panel
/// thread loads its own copy of the app's dictionaries (styles, row templates, icons, the palette) instead of reaching
/// into the main thread's. On the main thread everything still comes from Application.Resources.
/// </summary>
public static class ThreadResources
{
    [ThreadStatic] static ResourceDictionary? _dict;

    static readonly string[] Parts = ["UI/Controls.xaml", "UI/Objects.xaml", "UI/Cards.xaml", "UI/Templates.xaml", "UI/PanelRows.xaml"];

    /// <summary>This thread's copy, or null on the main thread.</summary>
    public static ResourceDictionary? Dictionary => _dict;

    /// <summary>Loads the dictionaries for the current (non-main) UI thread.</summary>
    public static void Init()
    {
        var d = new ResourceDictionary();
        d.MergedDictionaries.Add(Palettes.Build(Palettes.Get(ThemeManager.PaletteKey), ThemeManager.IsDark));
        foreach (var part in Parts)
            d.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/SweaxClip;component/{part}", UriKind.Absolute) });
        _dict = d;
    }

    /// <summary>Swaps in the current palette and mode (after the theme changed on the main thread).</summary>
    public static void ApplyTheme()
    {
        if (_dict == null) return;
        _dict.MergedDictionaries[0] = Palettes.Build(Palettes.Get(ThemeManager.PaletteKey), ThemeManager.IsDark);
    }

    /// <summary>A resource for code that has no element to look from (icons and hues of a kind of clip).</summary>
    public static object? Find(string key) => _dict != null ? (_dict.Contains(key) ? _dict[key] : FindMerged(_dict, key)) : Application.Current?.TryFindResource(key);

    static object? FindMerged(ResourceDictionary d, string key)
    {
        for (int i = d.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var m = d.MergedDictionaries[i];
            if (m.Contains(key)) return m[key];
            if (FindMerged(m, key) is { } hit) return hit;
        }
        return null;
    }
}
