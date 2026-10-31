using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace SweaxClip.Core;

public static class TimeText
{
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public static DateTime Local(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    public static string Relative(long ms)
    {
        var t = Local(ms);
        var span = DateTime.Now - t;
        if (span.TotalSeconds < 45) return Loc.T("Now");
        if (span.TotalMinutes < 60) return Loc.Format("MinutesAgo", (int)Math.Max(1, span.TotalMinutes));
        if (span.TotalHours < 6 && t.Date == DateTime.Today) return Loc.Format("HoursAgo", (int)span.TotalHours);
        return t.ToString("HH:mm", Loc.Culture);
    }

    /// <summary>Compact form for the panel's time rail: now · 4m · 2h · yest. · Tue · 12 Oct.</summary>
    public static string Short(long ms)
    {
        var t = Local(ms);
        var span = DateTime.Now - t;
        int days = (int)(DateTime.Today - t.Date).TotalDays;
        if (span.TotalSeconds < 45) return Loc.T("NowShort");
        if (span.TotalMinutes < 60) return Loc.Format("MinShort", (int)Math.Max(1, span.TotalMinutes));
        if (days == 0) return Loc.Format("HourShort", (int)Math.Max(1, span.TotalHours));
        if (days == 1) return Loc.T("YesterdayShort");
        if (days < 7) return t.ToString("ddd", Loc.Culture);
        return t.ToString(t.Year == DateTime.Today.Year ? "d MMM" : "MMM yyyy", Loc.Culture);
    }

    public static string Absolute(long ms) => ms <= 0 ? "—" : Local(ms).ToString("d MMMM yyyy, HH:mm:ss", Loc.Culture);

    public static (string label, string sub) DayHeader(DateTime day)
    {
        var today = DateTime.Today;
        int diff = (int)(today - day.Date).TotalDays;
        string full = day.ToString("d MMMM yyyy", Loc.Culture);
        string weekday = Loc.Culture.TextInfo.ToTitleCase(day.ToString("dddd", Loc.Culture));
        return diff switch
        {
            0 => (Loc.T("Today"), $"{full} · {weekday}"),
            1 => (Loc.T("Yesterday"), $"{full} · {weekday}"),
            < 7 => (weekday, full),
            _ => (day.ToString(day.Year == today.Year ? "d MMMM" : "d MMMM yyyy", Loc.Culture), weekday),
        };
    }
}

/// <summary>Search normalisation: case- and diacritic-insensitive, with Turkish letters folded.</summary>
public static class SearchText
{
    public static string Normalize(string? s, int max = 32_000)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Length > max) s = s[..max];
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            char c = ch switch
            {
                'İ' or 'I' or 'ı' or 'î' or 'Î' => 'i',
                'Ş' or 'ş' => 's',
                'Ğ' or 'ğ' => 'g',
                'Ü' or 'ü' or 'û' or 'Û' => 'u',
                'Ö' or 'ö' => 'o',
                'Ç' or 'ç' => 'c',
                'â' or 'Â' => 'a',
                _ => char.ToLowerInvariant(ch),
            };
            sb.Append(c);
        }
        string folded = sb.ToString();
        if (!folded.Any(ch => ch > 127)) return folded;
        // strip remaining accents: é → e, ñ → n, ø stays
        var d = folded.Normalize(NormalizationForm.FormD);
        var o = new StringBuilder(d.Length);
        foreach (char ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) o.Append(ch);
        return o.ToString().Normalize(NormalizationForm.FormC);
    }
}

public static class Classifier
{
    static readonly Regex ColorRx = new(
        @"^(#(?:[0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})|(?:rgba?|hsla?)\(\s*[\d.]+%?\s*[,\s]\s*[\d.]+%?\s*[,\s]\s*[\d.]+%?\s*(?:[,/]\s*[\d.]+%?\s*)?\))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex Keywords = new(
        @"\b(function|const|let|var|def|class|import|export|return|public|private|protected|static|void|async|await|namespace|using|lambda|struct|interface|package|elif|foreach|println|printf|console\.log|self\.|this\.|null|nullptr|true|false|None|SELECT|INSERT|UPDATE|DELETE|WHERE|JOIN|local|then|end)\b",
        RegexOptions.Compiled);

    static readonly Regex Operators = new(@"(=>|->|::|==|!=|&&|\|\||<=|>=|\+=|-=|\(\)|</|/>|\$\{|#include|#define|\bfn\b|:=)", RegexOptions.Compiled);

    static readonly Regex ShellCmd = new(
        @"^(?:sudo\s+)?(npm|npx|pnpm|yarn|git|cd|pip|pip3|python|py|node|dotnet|docker|kubectl|curl|wget|winget|choco|cargo|go|java|javac|gradle|mvn|ssh|scp|ls|dir|mkdir|rm|del|cat|echo|code|gh|flutter|php|composer|ffmpeg|Get-[A-Za-z]+|Set-[A-Za-z]+|New-[A-Za-z]+|Invoke-[A-Za-z]+)\s+\S",
        RegexOptions.Compiled);

    // One line of code: a call — print("hi"), console.log(x);, os.path.join(a, b)
    static readonly Regex CallLine = new(
        @"^[A-Za-z_$][\w$]*(?:(?:\.|::|->|\?\.)[A-Za-z_$][\w$]*)*(?:<[\w\s,.<>?]*>)?\(.*\)\s*(?:\.[\w$]+\(.*\)\s*)*;?$", RegexOptions.Compiled);

    // an assignment — x = 5, const a = [1, 2];, game.Workspace.Part.Transparency = 1
    static readonly Regex AssignLine = new(
        @"^(?:(?:let|const|var|local|val|auto|int|string|float|double|bool|char|long|dim|my|export)\s+)?[A-Za-z_$][\w$]*(?:(?:\.|::|->)[A-Za-z_$][\w$]*|\[[^\]]*\])*\s*(?:=|\+=|-=|\*=|/=|:=|\?\?=|\|\|=)\s*(?<rhs>\S.*)$", RegexOptions.Compiled);

    // a line that opens with a statement
    static readonly Regex StatementLine = new(
        @"^(?:import\s+[\w.]+(?:\s+as\s+\w+)?(?:\s*,\s*[\w.]+(?:\s+as\s+\w+)?)*;?$|import\s+.+\s+from\s+['""].+['""];?$|import\s+['""].+['""];?$|from\s+[\w.]+\s+import\s+[\w*]|using\s+[\w.]+\s*;$|#include\s*[<""]|#define\s+\w|#!/|def\s+\w+\s*\(|async\s+def\s|function\s*\*?\s*\w*\s*\(|class\s+\w+\s*[:({<]|return(?:\s+.+)?;$|(?:public|private|protected|internal)\s+\w.*[;{(=]|static\s+\w+\s+\w+\s*\(|(?:if|for|while|foreach|switch)\s*\(.*\)\s*\{?$|fn\s+\w+\s*\(|func\s+\w+\s*\(|package\s+\w+(?:\.\w+)+;?$|package\s+\w+;$|<\?php|@[A-Za-z][\w.]*\(.*\)$)", RegexOptions.Compiled);

    static readonly Regex SqlLine = new(
        @"^(?:SELECT\s.+\sFROM\s|INSERT\s+INTO\s|UPDATE\s+\w+\s+SET\s|DELETE\s+FROM\s|CREATE\s+(?:TABLE|INDEX|VIEW)\s|ALTER\s+TABLE\s|DROP\s+TABLE\s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex TagLine = new(@"^<([A-Za-z][\w-]*)(?:\s[^<>]*)?>.*</\1>$|^<[A-Za-z][\w-]*(?:\s[^<>]*)?/>$|^<!DOCTYPE\s", RegexOptions.Compiled);
    static readonly Regex JsonLine = new(@"^\{\s*""[^""]+""\s*:.*\}$|^\[\s*(?:""|\d|\{|\[|true|false|null).*\]$", RegexOptions.Compiled);
    static readonly Regex CssLine = new(@"^[^{}]{1,120}\{\s*[\w-]+\s*:[^{}]+;?\s*\}$", RegexOptions.Compiled);

    // prose inside an assignment's right-hand side: two words in a row, or a number followed by a word ("50 TL")
    static readonly Regex ProseRhs = new(@"\p{L}{2,}\s+\p{L}{2,}|\d\s+\p{L}", RegexOptions.Compiled);
    static readonly Regex Quoted = new(@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`[^`]*`", RegexOptions.Compiled);

    /// <summary>True when one line, on its own, reads as code rather than prose.</summary>
    static bool IsCodeLine(string line)
    {
        string l = line.Trim();
        if (l.Length < 3 || l.Length > 600) return false;
        if (ShellCmd.IsMatch(l) || StatementLine.IsMatch(l) || SqlLine.IsMatch(l) || TagLine.IsMatch(l) || JsonLine.IsMatch(l) || CssLine.IsMatch(l)) return true;
        if (CallLine.IsMatch(l)) return true;
        var m = AssignLine.Match(l);
        if (m.Success)
        {
            string rhs = Quoted.Replace(m.Groups["rhs"].Value, "\"\"");
            return !ProseRhs.IsMatch(rhs) && !rhs.TrimEnd().EndsWith('.') || rhs.TrimEnd().EndsWith(';');
        }
        return false;
    }

    public static ClipKind Classify(string text)
    {
        string t = text.Trim();
        if (t.Length == 0) return ClipKind.Text;

        bool singleLine = t.IndexOfAny(['\n', '\r']) < 0;
        if (singleLine && t.Length < 4096 && IsLink(t)) return ClipKind.Link;
        if (singleLine && t.Length < 64 && ColorRx.IsMatch(t)) return ClipKind.Color;
        if (LooksLikeCode(t, singleLine)) return ClipKind.Code;
        return ClipKind.Text;
    }

    public static bool IsLink(string t)
    {
        if (t.Contains(' ')) return false;
        if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && t.Length > 6 && t.IndexOf('.', 4) > 0) return true;
        return Uri.TryCreate(t, UriKind.Absolute, out var u)
               && (u.Scheme is "http" or "https" or "ftp" or "mailto" or "file" || (u.Scheme.Length > 2 && t.Contains("://")))
               && !(u.IsFile && !t.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
    }

    static bool LooksLikeCode(string t, bool singleLine)
    {
        if (t.Length > 400_000) return false;
        if (singleLine) return IsCodeLine(t);

        var lines = t.Replace("\r\n", "\n").Split('\n');
        int nonEmpty = 0, codeEnds = 0, indented = 0, codeLines = 0;
        foreach (var raw in lines.Take(400))
        {
            string l = raw.TrimEnd();
            if (l.Length == 0) continue;
            nonEmpty++;
            char last = l[^1];
            if (last is ';' or '{' or '}' or ')' or ']' or ',' or ':' && !l.EndsWith("..")) codeEnds++;
            if (raw.StartsWith("    ") || raw.StartsWith('\t')) indented++;
            if (codeLines < 200 && IsCodeLine(l)) codeLines++;
        }
        if (nonEmpty == 1) return IsCodeLine(t);
        if (nonEmpty < 2) return false;
        // most lines are code on their own: a short script such as "x = 5 / print(x)"
        if (codeLines * 2 >= nonEmpty + 1) return true;
        string sample = t.Length > 20_000 ? t[..20_000] : t;
        int kw = Keywords.Matches(sample).Count;
        int ops = Operators.Matches(sample).Count;
        int braces = sample.Count(c => c is '{' or '}' or ';');
        int letters = sample.Count(char.IsLetter);
        double symbolRatio = (double)sample.Count(c => "{}[]();=<>".Contains(c)) / Math.Max(1, sample.Length);

        double score = codeEnds * 1.2 + indented * 0.6 + kw * 0.8 + ops * 1.5 + braces * 0.3;
        if (symbolRatio > 0.04) score += nonEmpty;
        if (letters > 0 && sample.Count(c => c == '.') > nonEmpty * 1.5 && symbolRatio < 0.01) score *= 0.5; // prose
        return score >= nonEmpty * 1.6 + 2;
    }
}

public static class ColorParse
{
    public static Color? TryParse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        try
        {
            if (s.StartsWith('#'))
            {
                string h = s[1..];
                if (h.Length is 3 or 4) h = string.Concat(h.Select(c => $"{c}{c}"));
                uint v = uint.Parse(h, NumberStyles.HexNumber);
                return h.Length == 6
                    ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
                    : Color.FromArgb((byte)v, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8));
            }
            var m = Regex.Match(s, @"^(rgba?|hsla?)\(\s*([\d.]+)(%?)\s*[,\s]\s*([\d.]+)(%?)\s*[,\s]\s*([\d.]+)(%?)\s*(?:[,/]\s*([\d.]+)(%?)\s*)?\)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            double a = 1;
            if (m.Groups[8].Success && m.Groups[8].Value.Length > 0)
            {
                a = double.Parse(m.Groups[8].Value, CultureInfo.InvariantCulture);
                if (m.Groups[9].Value == "%") a /= 100;
            }
            double p1 = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double p2 = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            double p3 = double.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
            byte A = (byte)Math.Clamp(a * 255, 0, 255);
            if (m.Groups[1].Value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                byte C(double v, string pct) => (byte)Math.Clamp(pct == "%" ? v * 2.55 : v, 0, 255);
                return Color.FromArgb(A, C(p1, m.Groups[3].Value), C(p2, m.Groups[5].Value), C(p3, m.Groups[7].Value));
            }
            var (r, g, b) = HslToRgb(p1, p2 / 100, p3 / 100);
            return Color.FromArgb(A, r, g, b);
        }
        catch { return null; }
    }

    public static Brush? TryBrush(string? s)
    {
        if (TryParse(s) is not { } c) return null;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static string Describe(string? s)
    {
        if (TryParse(s) is not { } c) return "";
        var (h, sat, l) = RgbToHsl(c);
        string hex = c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
        return $"{hex}   rgb({c.R}, {c.G}, {c.B})   hsl({h:0}, {sat * 100:0}%, {l * 100:0}%)";
    }

    static (byte, byte, byte) HslToRgb(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double T(double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1 / 6.0) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
            return p;
        }
        return ((byte)Math.Round(T(h + 1 / 3.0) * 255), (byte)Math.Round(T(h) * 255), (byte)Math.Round(T(h - 1 / 3.0) * 255));
    }

    static (double h, double s, double l) RgbToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }
}
