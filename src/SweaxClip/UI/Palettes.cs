using System.Windows;
using System.Windows.Media;

namespace SweaxClip.UI;

/// <summary>The seed colours of one palette in one mode; every other token is derived from these.</summary>
/// <para><c>TextHue</c>: colour of text clips on the rail; null means the accent, "neutral" a soft ink.</para>
public sealed record Tones(string Desk, string Surface, string Ink, string Accent, string Paper, string Manila, string Slate, string? TextHue = null);

/// <summary>A named colour palette with a light and a dark variant.</summary>
public sealed record Palette(string Key, string NameEn, string NameTr, Tones Light, Tones Dark)
{
    public string Name => Core.Loc.Culture.TwoLetterISOLanguageName == "tr" ? NameTr : NameEn;
}

/// <summary>
/// SweaxClip's palettes. Each defines seven seed colours per mode — desk, surface, ink, accent and the
/// three materials (paper for text, manila for files, slate for code) — and <see cref="Build"/> derives
/// every brush the app uses from them, so a palette can never forget a token.
/// </summary>
public static class Palettes
{
    public static readonly IReadOnlyList<Palette> All =
    [
        new("ember", "Ember", "Kor",
            new("#EEEBE5", "#FFFFFF", "#23201D", "#E4572E", "#FFFDF8", "#EAD7A8", "#1D2027", "neutral"),
            new("#171615", "#262422", "#F2EFE9", "#FF6A3D", "#2A2826", "#3B3426", "#1A1D23", "neutral")),
        new("graphite", "Graphite", "Grafit",
            new("#E6EAF0", "#FFFFFF", "#1B1F24", "#2F6BEA", "#FFFFFF", "#E1E6EE", "#1B1F27"),
            new("#111316", "#1C1F24", "#E9EDF2", "#5B8CFF", "#20242A", "#272C34", "#15181D")),
        new("midnight", "Midnight", "Gece",
            new("#E9E6F8", "#FFFFFF", "#1F1D33", "#6C55E0", "#FFFFFF", "#E6E2F5", "#1D1B2E"),
            new("#121120", "#1D1B2E", "#EEEBFA", "#9D8CFF", "#232036", "#2B2842", "#161423")),
        new("sage", "Sage", "Adaçayı",
            new("#E5ECE2", "#FFFFFF", "#1D241F", "#2E8B62", "#FDFEFB", "#E0E8D9", "#1C2420"),
            new("#121613", "#1C221E", "#E8EFE9", "#4CC38A", "#212823", "#29312A", "#141A16")),
        new("ocean", "Ocean", "Okyanus",
            new("#E0ECEE", "#FFFFFF", "#16252A", "#0E8FA3", "#FBFEFE", "#DBEAED", "#172227"),
            new("#0E1517", "#172225", "#E5F1F3", "#2BC4D8", "#1B282C", "#213237", "#10181B")),
        new("rose", "Rose", "Gül",
            new("#F4E6EB", "#FFFFFF", "#2A1E24", "#D6336C", "#FFFCFD", "#F1DDE4", "#241C22"),
            new("#181215", "#261C21", "#F6ECF0", "#FF6B9A", "#2C2127", "#37282F", "#1B1418")),
        new("mono", "Mono", "Mono",
            new("#EFEFEF", "#FFFFFF", "#161616", "#161616", "#FFFFFF", "#E4E4E4", "#1A1A1A"),
            new("#0F0F0F", "#1C1C1C", "#EDEDED", "#F2F2F2", "#222222", "#2B2B2B", "#141414")),
    ];

    public static Palette Get(string? key) => All.FirstOrDefault(p => p.Key == key) ?? All[0];

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static double Lum(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
    static readonly Color White = Colors.White, Black = Colors.Black;

    /// <summary>Text colours that read on a material: near-black on light ones, near-white on dark ones.</summary>
    static (Color ink, Color ink2) InkOn(Color m) => Lum(m) > 0.5
        ? (Mix(m, Black, 0.86), A(Mix(m, Black, 0.86), 0x9E))
        : (Mix(m, White, 0.88), A(Mix(m, White, 0.88), 0x8C));

    /// <summary>Every token of the app for <paramref name="p"/> in light or dark.</summary>
    public static ResourceDictionary Build(Palette p, bool dark)
    {
        var t = dark ? p.Dark : p.Light;
        Color desk = C(t.Desk), surface = C(t.Surface), ink = C(t.Ink), accent = C(t.Accent), paper = C(t.Paper), manila = C(t.Manila), slate = C(t.Slate);
        var (paperInk, paperInk2) = InkOn(paper);
        var (manilaInk, manilaInk2) = InkOn(manila);
        Color accentInk = Lum(accent) > 0.6 ? Mix(accent, Black, 0.88) : White;

        var c = new Dictionary<string, Color>
        {
            ["Desk"] = desk,
            ["DeskEdge"] = Mix(desk, ink, 0.04),
            ["Surface"] = surface,
            ["Surface2"] = Mix(surface, desk, 0.4),
            ["SurfaceHover"] = A(ink, dark ? (byte)0x14 : (byte)0x0F),
            ["SurfacePress"] = A(ink, dark ? (byte)0x1F : (byte)0x1A),
            ["Line"] = A(ink, 0x14),
            ["Line2"] = A(ink, 0x24),
            ["Ink"] = ink,
            ["Ink2"] = A(ink, 0xA6),
            ["Ink3"] = A(ink, 0x6E),
            ["Accent"] = accent,
            ["AccentHover"] = Mix(accent, dark ? White : Black, dark ? 0.14 : 0.1),
            ["AccentInk"] = accentInk,
            ["AccentSoft"] = A(accent, dark ? (byte)0x26 : (byte)0x1F),
            ["Smoke"] = dark ? A(Black, 0x99) : A(ink, 0x59),
            ["ChipFill"] = A(surface, 0xF2),
            ["Paper"] = paper,
            ["PaperInk"] = paperInk,
            ["PaperInk2"] = paperInk2,
            ["PaperLine"] = A(paperInk, 0x15),
            ["Manila"] = manila,
            ["ManilaDeep"] = Mix(manila, Black, 0.08),
            ["ManilaInk"] = manilaInk,
            ["ManilaInk2"] = manilaInk2,
            ["Slate"] = slate,
            ["SlateInk"] = C("#E6E8EE"),
            ["SlateInk2"] = C("#99E6E8EE"),
            ["SynKeyword"] = C("#FF8A65"),
            ["SynString"] = C("#A5D6A7"),
            ["SynComment"] = C("#7C8594"),
            ["SynNumber"] = C("#FFD180"),
            ["SynType"] = C("#80CBC4"),
            ["Danger"] = dark ? C("#FF7A5C") : C("#C8402A"),
            ["ShadowTint"] = dark ? Black : Mix(desk, Black, 0.8),
            ["PanelEdge"] = A(ink, dark ? (byte)0x22 : (byte)0x21),
            ["PanelGlint"] = dark ? A(White, 0x17) : White,
            ["RowHover"] = A(ink, dark ? (byte)0x0C : (byte)0x09),
            ["Rail"] = A(ink, 0x1C),
            ["Field"] = A(ink, dark ? (byte)0x0A : (byte)0x07),
            // one hue per kind of clip; "all" and "pinned" follow the palette's accent
            ["Hue.Text"] = t.TextHue == "neutral" ? (dark ? Mix(ink, desk, 0.12) : Mix(ink, desk, 0.42)) : t.TextHue is { } th ? C(th) : accent,
            ["Hue.Link"] = dark ? C("#7DA8FF") : C("#2F6BEA"),
            ["Hue.Code"] = dark ? C("#5CD6B4") : C("#0E8F70"),
            ["Hue.Image"] = dark ? C("#F48FB1") : C("#C2416D"),
            ["Hue.Screenshot"] = dark ? C("#A78BFA") : C("#6D4FD8"),
            ["Hue.Files"] = dark ? C("#D7B46A") : C("#96701E"),
            ["Hue.All"] = accent,
            ["Hue.Pinned"] = accent,
        };

        var d = new ResourceDictionary { ["SweaxTheme"] = p.Key, ["IsDarkTheme"] = dark, ["ShadowOpacity"] = dark ? 0.55 : 0.22 };
        foreach (var (key, color) in c)
        {
            d[key + "Color"] = color;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            d[key] = brush;
        }
        Color top = dark ? Mix(desk, surface, 0.5) : surface, bottom = dark ? desk : Mix(surface, desk, 0.45);
        d["PanelFill"] = Frozen(new LinearGradientBrush(new GradientStopCollection { new(top, 0), new(bottom, 1) }, new Point(0, 0), new Point(0, 1)));
        d["Hue.Color"] = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(dark ? C("#FF6A3D") : C("#E4572E"), 0), new(dark ? C("#F2B84B") : C("#D99A1E"), 0.35),
            new(dark ? C("#5CD6B4") : C("#0E8F70"), 0.7), new(dark ? C("#7DA8FF") : C("#2F6BEA"), 1),
        }, new Point(0, 0), new Point(1, 1)));
        return d;
    }

    static Brush Frozen(Brush b) { b.Freeze(); return b; }

    /// <summary>A small picture of the palette for the settings swatch: desk, a surface card, paper, slate and the accent.</summary>
    public static (Brush desk, Brush surface, Brush accent, Brush paper, Brush slate, Brush edge) Preview(Palette p, bool dark)
    {
        var t = dark ? p.Dark : p.Light;
        Brush B(Color c) => Frozen(new SolidColorBrush(c));
        return (B(C(t.Desk)), B(C(t.Surface)), B(C(t.Accent)), B(C(t.Paper)), B(C(t.Slate)), B(A(C(t.Ink), 0x22)));
    }
}
