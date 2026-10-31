using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SweaxClip.Core;

namespace SweaxClip.UI;

/// <summary>Colours that belong to a site: a known brand tone, or a stable hue derived from the host name.</summary>
public static class SiteColor
{
    static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["github.com"] = "#24292F", ["youtube.com"] = "#E33B2E", ["youtu.be"] = "#E33B2E", ["google.com"] = "#3A7BD5",
        ["figma.com"] = "#A259FF", ["open.spotify.com"] = "#1DB954", ["spotify.com"] = "#1DB954", ["x.com"] = "#14171A",
        ["twitter.com"] = "#1D9BF0", ["wikipedia.org"] = "#636466", ["stackoverflow.com"] = "#F48024", ["discord.com"] = "#5865F2",
        ["discord.gg"] = "#5865F2", ["reddit.com"] = "#FF4500", ["linkedin.com"] = "#0A66C2", ["notion.so"] = "#2F3437",
        ["amazon.com"] = "#FF9900", ["developer.mozilla.org"] = "#1F2A44", ["instagram.com"] = "#D6336C", ["roblox.com"] = "#335FFF",
        ["npmjs.com"] = "#CB3837", ["microsoft.com"] = "#0F6CBD", ["apple.com"] = "#555555", ["twitch.tv"] = "#9146FF",
    };
    static readonly Dictionary<string, Brush> Cache = new();

    public static Brush For(string host)
    {
        if (string.IsNullOrEmpty(host)) host = "?";
        lock (Cache)
        {
            if (Cache.TryGetValue(host, out var b)) return b;
            string key = Known.Keys.FirstOrDefault(k => host.Equals(k, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + k, StringComparison.OrdinalIgnoreCase)) ?? "";
            Color c = key.Length > 0 ? (Color)ColorConverter.ConvertFromString(Known[key]) : FromHash(host);
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return Cache[host] = brush;
        }
    }

    static Color FromHash(string s)
    {
        uint h = 2166136261;
        foreach (char ch in s) h = (h ^ ch) * 16777619;
        double hue = h % 360;
        return Hsl(hue, 0.52, 0.46);
    }

    static Color Hsl(double h, double s, double l)
    {
        h /= 360;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double T(double t) { if (t < 0) t += 1; if (t > 1) t -= 1; return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p; }
        return Color.FromRgb((byte)(T(h + 1 / 3.0) * 255), (byte)(T(h) * 255), (byte)(T(h - 1 / 3.0) * 255));
    }

    public static bool IsLight(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0 > 0.58;
}

/// <summary>
/// A small, language-agnostic highlighter for code cards: comments, strings, numbers, keywords and
/// PascalCase types, painted onto a TextBlock as runs.
/// </summary>
public static class CodeView
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(CodeView), new PropertyMetadata(null, (d, e) => { if (d is TextBlock tb) Paint(tb, e.NewValue as string ?? ""); }));
    public static void SetSource(DependencyObject d, string v) => d.SetValue(SourceProperty, v);
    public static string GetSource(DependencyObject d) => (string)d.GetValue(SourceProperty);

    static readonly Regex Token = new(
        @"(?<c>//[^\n]*|#(?!include|define|region)[^\n]*|/\*.*?\*/|--[^\n]*)|(?<s>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\\n])*'|`[^`]*`)|(?<n>\b\d+(?:\.\d+)?\b)|(?<k>\b(?:public|private|protected|internal|static|readonly|const|var|let|async|await|return|if|else|for|foreach|while|do|switch|case|break|continue|new|class|struct|interface|enum|namespace|using|import|export|from|def|function|fn|lambda|true|false|null|None|nullptr|this|self|void|int|string|bool|double|float|try|catch|finally|throw|in|of|SELECT|FROM|WHERE|GROUP|BY|ORDER|INSERT|UPDATE|DELETE|JOIN|AS|AND|OR|NOT|IS|DESC|ASC|local|then|end|elif|git|npm|dotnet|cd|commit|install)\b)|(?<t>\b[A-Z][a-zA-Z0-9]+\b)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    static readonly Brush Plain = Frozen("#E6E8EE"), Keyword = Frozen("#FF8A65"), Str = Frozen("#A5D6A7"),
        Comment = Frozen("#7C8594"), Number = Frozen("#FFD180"), Type = Frozen("#80CBC4");

    static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    static void Paint(TextBlock tb, string code)
    {
        tb.Inlines.Clear();
        if (code.Length > 4000) code = code[..4000];
        int pos = 0;
        foreach (Match m in Token.Matches(code))
        {
            if (m.Index > pos) tb.Inlines.Add(new Run(code[pos..m.Index]) { Foreground = Plain });
            Brush b = m.Groups["c"].Success ? Comment : m.Groups["s"].Success ? Str : m.Groups["n"].Success ? Number : m.Groups["k"].Success ? Keyword : Type;
            tb.Inlines.Add(new Run(m.Value) { Foreground = b });
            pos = m.Index + m.Length;
        }
        if (pos < code.Length) tb.Inlines.Add(new Run(code[pos..]) { Foreground = Plain });
    }
}

/// <summary>Resource lookups for the drawn object icons.</summary>
public static class ObjectIcons
{
    public static ImageSource? For(Scope s) => Find(s switch
    {
        Scope.All => "Obj.Stack",
        Scope.Text => "Obj.Text",
        Scope.Links => "Obj.Link",
        Scope.Images => "Obj.Image",
        Scope.Screenshots => "Obj.Screenshot",
        Scope.Files => "Obj.Files",
        Scope.Code => "Obj.Code",
        Scope.Colors => "Obj.Color",
        Scope.Pinned => "Obj.Pinned",
        Scope.Trash => "Obj.Trash",
        _ => "Obj.Stack",
    });

    public static ImageSource? For(ClipKind k) => Find(k switch
    {
        ClipKind.Link => "Obj.Link",
        ClipKind.Code => "Obj.Code",
        ClipKind.Color => "Obj.Color",
        ClipKind.Image => "Obj.Image",
        ClipKind.Screenshot => "Obj.Screenshot",
        ClipKind.Files => "Obj.Files",
        _ => "Obj.Text",
    });

    static ImageSource? Find(string key) => Application.Current?.TryFindResource(key) as ImageSource;
}

/// <summary>Picks the card for a clip ("Card.Text", "Card.Image", …).</summary>
public sealed class CardSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ClipItem c || container is not FrameworkElement fe) return null;
        string key = c.Kind switch
        {
            ClipKind.Files when c.Thumb != null => "Card.FilesVisual",
            _ => "Card." + c.Kind,
        };
        return fe.TryFindResource(key) as DataTemplate ?? fe.TryFindResource("Card.Text") as DataTemplate;
    }
}
