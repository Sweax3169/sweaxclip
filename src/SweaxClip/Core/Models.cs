using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SweaxClip.Core;

public enum ClipKind { Text = 0, Link = 1, Code = 2, Color = 3, Image = 4, Screenshot = 5, Files = 6 }

/// <summary>A browsing scope: a kind filter, the pinned view or the trash.</summary>
public enum Scope { All, Text, Links, Images, Screenshots, Files, Code, Colors, Pinned, Trash }

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class ClipItem : Observable
{
    public long Id { get; set; }
    public ClipKind Kind { get; set; }
    public string Hash { get; set; } = "";
    public string? Text { get; set; }
    public string? Html { get; set; }
    public string? Rtf { get; set; }
    public string? Blob { get; set; }
    public string? Thumb { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long Size { get; set; }
    public string? App { get; set; }
    public string? AppPath { get; set; }
    public string? Title { get; set; }
    public long Created { get; set; }
    public long Deleted { get; set; }
    public long TextLength { get; set; }
    /// <summary>List queries load only the head of long texts; the full row is fetched on demand.</summary>
    public bool IsPartial => TextLength > (Text?.Length ?? 0);

    long _used; public long Used { get => _used; set { if (Set(ref _used, value)) RefreshTime(); } }
    int _uses = 1; public int Uses { get => _uses; set { if (Set(ref _uses, value)) Raise(nameof(UsesLabel)); } }
    public string UsesLabel => _uses > 1 ? $"×{_uses}" : "";
    bool _pinned; public bool Pinned { get => _pinned; set => Set(ref _pinned, value); }
    string? _ocr; public string? Ocr { get => _ocr; set { if (Set(ref _ocr, value)) Raise(nameof(HasOcr)); } }
    public bool HasOcr => !string.IsNullOrWhiteSpace(_ocr);

    // ── presentation ──────────────────────────────────────────────────────────
    bool _isSelected; public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    bool _showIndex; public bool ShowIndex { get => _showIndex; set => Set(ref _showIndex, value); }
    int _index; public int Index { get => _index; set { if (Set(ref _index, value)) Raise(nameof(IndexLabel)); } }
    public string IndexLabel => _index is > 0 and < 10 ? _index.ToString() : "";

    ImageSource? _thumbImage; public ImageSource? ThumbImage { get => _thumbImage; set => Set(ref _thumbImage, value); }

    public ImageSource? AppIcon => IconCache.ForExe(AppPath);

    string _relTime = ""; public string RelTime { get => _relTime; private set => Set(ref _relTime, value); }
    public void RefreshTime() => RelTime = TimeText.Relative(Used);
    public string CreatedText => TimeText.Absolute(Created);
    public string UsedText => TimeText.Absolute(Used);

    public bool IsImage => Kind is ClipKind.Image or ClipKind.Screenshot;
    public bool IsVisual => IsImage || (Kind == ClipKind.Files && Thumb != null);
    public bool IsTextual => Kind is ClipKind.Text or ClipKind.Link or ClipKind.Code or ClipKind.Color;

    string? _preview;
    /// <summary>A compact, whitespace-collapsed excerpt used by list rows.</summary>
    public string Preview => _preview ??= MakePreview();

    string MakePreview()
    {
        if (Kind == ClipKind.Files) return string.Join("  ·  ", FileNames.Take(4));
        string t = Text ?? "";
        if (Kind == ClipKind.Code) return CodePreview(t);
        if (t.Length > 600) t = t[..600];
        var sb = new System.Text.StringBuilder(t.Length);
        bool space = false;
        foreach (char c in t)
        {
            if (char.IsWhiteSpace(c)) { if (!space && sb.Length > 0) sb.Append(' '); space = true; }
            else { sb.Append(c); space = false; }
        }
        return sb.ToString().TrimEnd();
    }

    static string CodePreview(string t)
    {
        var lines = t.Replace("\r\n", "\n").Split('\n');
        var keep = lines.SkipWhile(string.IsNullOrWhiteSpace).Take(6).ToArray();
        int indent = keep.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", keep.Select(l => (l.Length >= indent ? l[indent..] : l.TrimStart()).Replace("\t", "  ").TrimEnd()));
    }

    public string FullText => Kind == ClipKind.Files ? string.Join(Environment.NewLine, Files) : Text ?? "";

    public string[] Files => Kind == ClipKind.Files ? (Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];
    public IEnumerable<string> FileNames => Files.Select(f => System.IO.Path.GetFileName(f.TrimEnd('\\')) is { Length: > 0 } n ? n : f);
    public int FileCount => Files.Length;
    public ImageSource? FileIcon => Kind == ClipKind.Files && Files.Length > 0 ? IconCache.ForFile(Files[0]) : null;

    // links
    public string LinkHost => Kind == ClipKind.Link && Uri.TryCreate(LinkUrl, UriKind.Absolute, out var u) ? u.Host.StartsWith("www.") ? u.Host[4..] : u.Host : "";
    public string LinkRest
    {
        get
        {
            if (Kind != ClipKind.Link || !Uri.TryCreate(LinkUrl, UriKind.Absolute, out var u)) return Text ?? "";
            string rest = u.PathAndQuery + u.Fragment;
            return rest == "/" ? "" : Uri.UnescapeDataString(rest);
        }
    }
    public string LinkUrl
    {
        get
        {
            string t = (Text ?? "").Trim();
            return t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + t : t;
        }
    }

    public Brush SiteBrush => UI.SiteColor.For(LinkHost);
    public string SiteInitial => LinkHost.Length > 0 ? char.ToUpperInvariant(LinkHost[0]).ToString() : "#";

    // colors
    public Brush? Swatch => Kind == ClipKind.Color ? ColorParse.TryBrush(Text) : null;
    public string ColorDetail => Kind == ClipKind.Color ? ColorParse.Describe(Text) : "";
    public string ColorHex => Kind == ClipKind.Color && ColorParse.TryParse(Text) is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "";
    public string ColorRgb => Kind == ClipKind.Color && ColorParse.TryParse(Text) is { } c ? $"rgb {c.R} {c.G} {c.B}" : "";
    /// <summary>Black or white ink that reads on top of the swatch.</summary>
    public Brush SwatchInk => Kind == ClipKind.Color && ColorParse.TryParse(Text) is { } c && UI.SiteColor.IsLight(c) ? InkDark : InkLight;
    static readonly Brush InkDark = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x1A, 0x17, 0x14)));
    static readonly Brush InkLight = Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)));
    static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    // code
    public string CodeSnippet
    {
        get
        {
            if (Kind != ClipKind.Code) return "";
            var lines = (Text ?? "").Replace("\r\n", "\n").Replace("\t", "    ").Split('\n').SkipWhile(string.IsNullOrWhiteSpace).Take(10).ToArray();
            int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            return string.Join("\n", lines.Select(l => (l.Length >= indent ? l[indent..] : l.TrimStart()).TrimEnd()));
        }
    }
    public string LineNumbers => Kind == ClipKind.Code ? string.Join("\n", Enumerable.Range(1, Math.Max(1, CodeSnippet.Count(ch => ch == '\n') + 1))) : "";

    public ImageSource? KindIcon => UI.ObjectIcons.For(Kind);
    public string AppInitial => string.IsNullOrEmpty(App) ? "·" : char.ToUpperInvariant(App![0]).ToString();
    public string Headline => Kind switch
    {
        ClipKind.Link => LinkHost,
        ClipKind.Color => ColorHex,
        ClipKind.Image or ClipKind.Screenshot => Dimensions,
        ClipKind.Files => FileNames.FirstOrDefault() ?? "",
        _ => Preview.Length > 90 ? Preview[..90] : Preview,
    };

    // images
    public string Dimensions => Width > 0 ? $"{Width} × {Height}" : "";

    public string SizeText => Bytes.Format(Size);
    public string KindLabel => Loc.KindName(Kind);
    public string KindGlyph => Glyphs.ForKind(Kind);
    public string AppName => string.IsNullOrEmpty(App) ? Loc.T("UnknownApp") : App!;

    public string Stats
    {
        get
        {
            switch (Kind)
            {
                case ClipKind.Image or ClipKind.Screenshot: return $"{Dimensions}  ·  {SizeText}";
                case ClipKind.Files: return Loc.Format("FilesCount", FileCount);
                default:
                    string t = Text ?? "";
                    int lines = t.Length == 0 ? 0 : t.Count(c => c == '\n') + 1;
                    return lines > 1 ? Loc.Format("CharsLines", t.Length.ToString("N0"), lines.ToString("N0")) : Loc.Format("Chars", t.Length.ToString("N0"));
            }
        }
    }

    public void NotifyAll() => Raise(string.Empty);
}

/// <summary>Date separator inside a flattened list.</summary>
public sealed class HeaderRow
{
    public required string Label { get; init; }
    public required string Sub { get; init; }
}

/// <summary>A row of thumbnails used by the virtualized contact-sheet grid.</summary>
public sealed class GridRow
{
    public required IReadOnlyList<ClipItem> Items { get; init; }
    public required int Columns { get; init; }
}

public sealed class ScopeInfo : Observable
{
    public required Scope Scope { get; init; }
    public required string Glyph { get; init; }
    public ImageSource? Icon => UI.ObjectIcons.For(Scope);
    public string Label => Loc.ScopeName(Scope);
    public string Short => Loc.ScopeShort(Scope);
    int _count; public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(CountText)); } }
    public string CountText => _count.ToString("N0");
    bool _active; public bool Active { get => _active; set => Set(ref _active, value); }
    public int Number { get; init; }
}

public static class Bytes
{
    public static string Format(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => (b / 1024.0).ToString("0.#", Loc.Culture) + " KB",
        < 1024L * 1024 * 1024 => (b / 1024.0 / 1024).ToString("0.#", Loc.Culture) + " MB",
        _ => (b / 1024.0 / 1024 / 1024).ToString("0.##", Loc.Culture) + " GB",
    };
}

public static class Glyphs
{
    // Segoe Fluent Icons / Segoe MDL2 Assets code points
    public const string Unpin = "";
    public const string All = "", Text = "", Link = "", Image = "", Screenshot = "",
        Files = "", Code = "", Color = "", Pin = "", Pinned = "", Trash = "",
        Search = "", Settings = "", Open = "", Copy = "", Paste = "", Close = "",
        Restore = "", Folder = "", Save = "", Back = "", Expand = "", Pause = "",
        Play = "", Minimize = "", Maximize = "", Unmax = "", Info = "", Check = "",
        Keyboard = "", Shield = "", Database = "", History = "", Download = "", Grid = "", List = "";

    public static string ForKind(ClipKind k) => k switch
    {
        ClipKind.Text => Text,
        ClipKind.Link => Link,
        ClipKind.Code => Code,
        ClipKind.Color => Color,
        ClipKind.Image => Image,
        ClipKind.Screenshot => Screenshot,
        ClipKind.Files => Files,
        _ => Text,
    };

    public static string ForScope(Scope s) => s switch
    {
        Scope.All => History,
        Scope.Text => Text,
        Scope.Links => Link,
        Scope.Images => Image,
        Scope.Screenshots => Screenshot,
        Scope.Files => Files,
        Scope.Code => Code,
        Scope.Colors => Color,
        Scope.Pinned => Pinned,
        Scope.Trash => Trash,
        _ => All,
    };
}
