using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SweaxClip.Core;

namespace SweaxClip.Dev;

/// <summary>Synthetic archive used for screenshots and the demo mode. Never touches the real archive.</summary>
public static class DemoData
{
    static readonly string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    static readonly string Code = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe");
    static readonly string Firefox = @"C:\Program Files\Mozilla Firefox\firefox.exe";
    static readonly string Word = @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE";
    static readonly string Excel = @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE";
    static readonly string Explorer = @"C:\Windows\explorer.exe";
    static readonly string Notepad = @"C:\Windows\System32\notepad.exe";
    static readonly string Spotify = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
    static readonly string Discord = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Discord\Discord.exe");
    static readonly string Snip = @"C:\Windows\SystemApps\SnippingTool.exe";
    static readonly string Edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

    record Seed(ClipKind Kind, string? Text, string App, string? Exe, string Title, double MinutesAgo, bool Pinned = false, int Uses = 1, Func<BitmapSource>? Image = null, string? Ocr = null);

    public static void Fill(ClipStore store, bool tr)
    {
        string T(string en, string trText) => tr ? trText : en;
        const double H = 60, D = 1440;
        string files = Path.Combine(Paths.Data, "demo-files");
        Directory.CreateDirectory(files);
        string cover = Path.Combine(files, "cover-art-final.png");
        Save(Poster(), cover);

        var seeds = new List<Seed>
        {
            new(ClipKind.Text, T("Meeting moved to Thursday 14:30 — room 4B. Bring the Q4 numbers and the new onboarding flow.",
                "Toplantı perşembe 14:30'a alındı — 4B salonu. Q4 rakamlarını ve yeni onboarding akışını getir."), "Discord", Discord, "#studio — Discord", 1.2),
            new(ClipKind.Link, "https://github.com/Sweax3169/sweaxclip/releases/latest", "Google Chrome", Chrome, "Releases · Sweax3169/sweaxclip", 4),
            new(ClipKind.Screenshot, null, T("Snipping Tool", "Ekran Alıntısı Aracı"), Snip, "Snipping Tool", 9, Image: Dashboard,
                Ocr: T("Revenue 128,430 Active users 9,812 Conversion 4.7% Weekly report", "Gelir 128.430 Aktif kullanıcı 9.812 Dönüşüm %4,7 Haftalık rapor")),
            new(ClipKind.Code, "public async Task<Clip?> FindAsync(string query)\n{\n    var hits = await _store.SearchAsync(query, limit: 50);\n    return hits.OrderByDescending(h => h.Score)\n               .FirstOrDefault();\n}",
                "Visual Studio Code", Code, "ClipStore.cs — sweaxclip — Visual Studio Code", 16, Uses: 3),
            new(ClipKind.Color, "#0F6CBD", "Google Chrome", Chrome, "Brand — Figma", 22, Pinned: true, Uses: 7),
            new(ClipKind.Text, T("The best tool is the one you never have to think about. Copy it once — find it in ten years.",
                "En iyi araç, hakkında hiç düşünmek zorunda kalmadığındır. Bir kez kopyala — on yıl sonra bile bul."), "Microsoft Word", Word, "manifesto.docx — Word", 38),
            new(ClipKind.Link, "https://www.figma.com/design/k3Yz9/Sweax-Studios-Brand?node-id=12-482", "Google Chrome", Chrome, "Sweax Studios Brand — Figma", 52, Pinned: true),
            new(ClipKind.Image, null, "Google Chrome", Chrome, T("Moodboard — Pinterest", "Ruh hali panosu — Pinterest"), 70, Image: Sunset),
            new(ClipKind.Code, "git commit -m \"feat: unlimited history, instant search\"", T("Terminal", "Terminal"), @"C:\Windows\System32\cmd.exe", "Windows PowerShell", 95),
            new(ClipKind.Files, $"{cover}", T("File Explorer", "Dosya Gezgini"), Explorer, "Pictures", 130),
            new(ClipKind.Text, T("Tracking number: 1Z 999 AA1 01 2345 6784", "Kargo takip no: 1Z 999 AA1 01 2345 6784"), "Microsoft Edge", Edge, "Order details", 190),
            new(ClipKind.Color, "rgb(79, 124, 255)", "Visual Studio Code", Code, "theme.css — Visual Studio Code", 240),
            new(ClipKind.Screenshot, null, T("Snipping Tool", "Ekran Alıntısı Aracı"), Snip, "Snipping Tool", 300, Image: Invoice,
                Ocr: T("INVOICE #2026-1142 Sweax Studios Mixing & mastering 3 tracks Total 4,250.00 Due 15 Nov 2026",
                       "FATURA #2026-1142 Sweax Studios Miks ve mastering 3 parça Toplam 4.250,00 Son ödeme 15 Kasım 2026")),

            // yesterday
            new(ClipKind.Code, ".hero {\n  display: grid;\n  place-items: center;\n  background: #0A0A0B;\n  color: #0F6CBD;\n}", "Visual Studio Code", Code, "site.css — Visual Studio Code", D + 120),
            new(ClipKind.Link, "https://developer.mozilla.org/en-US/docs/Web/API/Clipboard_API", "Firefox", Firefox, "Clipboard API — MDN", D + 180, Uses: 2),
            new(ClipKind.Screenshot, null, T("Snipping Tool", "Ekran Alıntısı Aracı"), Snip, "Snipping Tool", D + 240, Image: Terminal,
                Ocr: "dotnet publish -c Release Build succeeded 0 Warning(s) 0 Error(s)"),
            new(ClipKind.Text, T("Q4 budget: 42,500 — approved by finance on 28 Oct.", "Q4 bütçesi: 42.500 — finans 28 Ekim'de onayladı."), "Microsoft Excel", Excel, "budget-2026.xlsx — Excel", D + 300),
            new(ClipKind.Files, @"C:\Users\Sweax\Music\Beats\night-drive_140bpm.wav" + "\n" + @"C:\Users\Sweax\Music\Beats\night-drive_stems.zip", T("File Explorer", "Dosya Gezgini"), Explorer, "Beats", D + 360),
            new(ClipKind.Color, "hsl(340, 82%, 52%)", "Google Chrome", Chrome, "Palette — Coolors", D + 420),
            new(ClipKind.Image, null, "Discord", Discord, "#design — Discord", D + 520, Image: Canvas),

            // this week
            new(ClipKind.Link, "https://open.spotify.com/playlist/37i9dQZF1DX0XUsuxWHRQd", "Spotify", Spotify, "Spotify", 3 * D + 200),
            new(ClipKind.Text, T("Lunch at 1? The new place on Bağdat Avenue.", "Öğlen 1'de yemek? Bağdat Caddesi'ndeki yeni yer."), "Discord", Discord, "Discord", 3 * D + 400),
            new(ClipKind.Image, null, "Google Chrome", Chrome, "Wallpapers", 4 * D + 100, Image: Mountains),
            new(ClipKind.Code, "SELECT app, COUNT(*) AS clips\nFROM clips\nWHERE deleted IS NULL\nGROUP BY app\nORDER BY clips DESC;", "Visual Studio Code", Code, "stats.sql — Visual Studio Code", 4 * D + 300),
            new(ClipKind.Text, T("Sweax Studios — Istanbul, Türkiye", "Sweax Studios — İstanbul, Türkiye"), "Notepad", Notepad, "notes.txt — Notepad", 5 * D),
            new(ClipKind.Color, "#0A0A0B", "Google Chrome", Chrome, "Brand — Figma", 5 * D + 60),

            // older
            new(ClipKind.Screenshot, null, T("Snipping Tool", "Ekran Alıntısı Aracı"), Snip, "Snipping Tool", 12 * D, Image: Chart),
            new(ClipKind.Link, "https://www.sweaxstudios.xyz", "Google Chrome", Chrome, "Sweax Studios", 19 * D, Pinned: true, Uses: 4),
            new(ClipKind.Text, T("Remember: export stems at 24-bit / 48 kHz.", "Unutma: stem'leri 24-bit / 48 kHz dışa aktar."), "Notepad", Notepad, "notes.txt — Notepad", 33 * D, Pinned: true),
            new(ClipKind.Screenshot, null, T("Snipping Tool", "Ekran Alıntısı Aracı"), Snip, "Snipping Tool", 61 * D, Image: Dashboard2),
        };

        long now = TimeText.Now;
        foreach (var s in seeds.AsEnumerable().Reverse())
        {
            long when = now - (long)(s.MinutesAgo * 60_000);
            var item = new ClipItem
            {
                Kind = s.Kind,
                App = s.App,
                AppPath = File.Exists(s.Exe) ? s.Exe : null,
                Title = s.Title,
                Created = when - (s.Uses > 1 ? 9 * (long)D * 60_000 : 0),
                Used = when,
            };
            if (s.Image != null)
            {
                var bmp = s.Image();
                item.Hash = ImageTools.PixelHash(bmp);
                var (rel, size) = ImageTools.SaveOriginal(bmp, null, item.Hash);
                item.Blob = rel;
                item.Size = size;
                item.Width = bmp.PixelWidth;
                item.Height = bmp.PixelHeight;
                item.Thumb = ImageTools.SaveThumb(Paths.Abs(rel), item.Hash);
            }
            else
            {
                item.Text = s.Text;
                item.Hash = ImageTools.TextHash(s.Kind == ClipKind.Files ? "F:" : "T:", s.Text!);
                item.Size = System.Text.Encoding.UTF8.GetByteCount(s.Text!);
                if (s.Kind == ClipKind.Files && s.Text == cover)
                {
                    item.Thumb = ImageTools.SaveThumb(cover, item.Hash);
                    (item.Width, item.Height) = ImageTools.ProbeSize(cover);
                    item.Size = new FileInfo(cover).Length;
                }
                if (s.Kind == ClipKind.Files && s.Text != cover) item.Size = 187_400_000;
            }
            long id = store.Insert(item, null, null, 0);
            if (s.Pinned) store.SetPinned(id, true);
            if (s.Ocr != null) store.SetOcr(id, s.Ocr, 2);
            for (int i = 1; i < s.Uses; i++) store.Touch(item.Hash, when);
        }
    }

    // ── synthetic images ────────────────────────────────────────────────────

    static readonly FontFamily Ui = new("Segoe UI");
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    static readonly FontFamily Display = new("Segoe UI Semibold");

    static BitmapSource Draw(int w, int h, Action<DrawingContext> paint)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) paint(dc);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var conv = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        conv.Freeze();
        return conv;
    }

    static Brush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    static void Text(DrawingContext dc, string s, double x, double y, double size, string color, FontFamily? f = null, FontWeight? weight = null)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(f ?? Ui, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size, B(color), 1.0);
        dc.DrawText(ft, new Point(x, y));
    }

    static void Save(BitmapSource b, string file)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    static BitmapSource Dashboard() => Draw(1920, 1080, dc =>
    {
        dc.DrawRectangle(B("#0E1014"), null, new Rect(0, 0, 1920, 1080));
        dc.DrawRectangle(B("#14171D"), null, new Rect(0, 0, 260, 1080));
        Text(dc, "orbit", 40, 40, 34, "#F2F2EE", Display);
        string[] nav = ["Overview", "Revenue", "Customers", "Reports", "Settings"];
        for (int i = 0; i < nav.Length; i++)
        {
            if (i == 0) dc.DrawRectangle(B("#22262E"), null, new Rect(20, 128 + i * 58, 220, 44));
            Text(dc, nav[i], 44, 138 + i * 58, 20, i == 0 ? "#FFFFFF" : "#7D828C");
        }
        Text(dc, "Weekly report", 320, 50, 44, "#F2F2EE", Ui, FontWeights.SemiBold);
        string[] k = ["Revenue", "Active users", "Conversion"], v = ["128,430", "9,812", "4.7%"];
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect(320 + i * 520, 150, 490, 190);
            dc.DrawRectangle(B("#151820"), new Pen(B("#262A33"), 2), r);
            Text(dc, k[i], r.X + 34, r.Y + 30, 22, "#8B909A");
            Text(dc, v[i], r.X + 34, r.Y + 72, 64, i == 0 ? "#3B8BEB" : "#F2F2EE", Display);
        }
        var chart = new Rect(320, 380, 1530, 640);
        dc.DrawRectangle(B("#151820"), new Pen(B("#262A33"), 2), chart);
        var rnd = new Random(7);
        for (int i = 0; i < 24; i++)
        {
            double hgt = 120 + rnd.NextDouble() * 380 + i * 6;
            dc.DrawRectangle(i % 6 == 5 ? B("#3B8BEB") : B("#3A3F4A"), null, new Rect(380 + i * 60, chart.Bottom - 50 - hgt, 34, hgt));
        }
    });

    static BitmapSource Dashboard2() => Draw(1920, 1080, dc =>
    {
        dc.DrawRectangle(B("#F4F4F1"), null, new Rect(0, 0, 1920, 1080));
        Text(dc, "Release checklist", 120, 90, 64, "#111111", Ui, FontWeights.Bold);
        string[] items = ["Unlimited history", "Win + V replacement", "Offline OCR", "Daily snapshots", "Contact-sheet library", "Turkish + English"];
        for (int i = 0; i < items.Length; i++)
        {
            dc.DrawRectangle(B("#111111"), null, new Rect(120, 250 + i * 110, 44, 44));
            Text(dc, "✓", 129, 252 + i * 110, 32, "#3B8BEB", Ui, FontWeights.Bold);
            Text(dc, items[i], 200, 252 + i * 110, 40, "#111111");
        }
        dc.DrawRectangle(B("#3B8BEB"), null, new Rect(1260, 0, 660, 1080));
        Text(dc, "v1.0", 1340, 760, 200, "#111111", Display);
    });

    static BitmapSource Invoice() => Draw(1400, 1000, dc =>
    {
        dc.DrawRectangle(B("#E9E9E4"), null, new Rect(0, 0, 1400, 1000));
        dc.DrawRectangle(B("#FFFFFF"), null, new Rect(200, 40, 1000, 960));
        Text(dc, "INVOICE", 260, 100, 72, "#111111", Display);
        Text(dc, "#2026-1142", 260, 200, 26, "#666666", Mono);
        Text(dc, "Sweax Studios", 900, 112, 26, "#111111", Ui, FontWeights.SemiBold);
        dc.DrawRectangle(B("#111111"), null, new Rect(260, 300, 880, 3));
        string[] rows = ["Mixing & mastering — 3 tracks", "Stem export, 24-bit / 48 kHz", "Revisions (2 rounds)"];
        string[] amt = ["3,600.00", "400.00", "250.00"];
        for (int i = 0; i < rows.Length; i++)
        {
            Text(dc, rows[i], 260, 340 + i * 70, 26, "#222222");
            Text(dc, amt[i], 1010, 340 + i * 70, 26, "#222222", Mono);
            dc.DrawRectangle(B("#E2E2DC"), null, new Rect(260, 390 + i * 70, 880, 2));
        }
        dc.DrawRectangle(B("#3B8BEB"), null, new Rect(700, 600, 440, 110));
        Text(dc, "Total", 730, 628, 30, "#111111", Ui, FontWeights.SemiBold);
        Text(dc, "4,250.00", 930, 622, 40, "#111111", Display);
        Text(dc, "Due 15 Nov 2026", 260, 640, 24, "#888888", Mono);
    });

    static BitmapSource Terminal() => Draw(1600, 900, dc =>
    {
        dc.DrawRectangle(B("#0C0C0C"), null, new Rect(0, 0, 1600, 900));
        dc.DrawRectangle(B("#1E1E1E"), null, new Rect(0, 0, 1600, 54));
        Text(dc, "PS C:\\dev\\sweaxclip>", 30, 90, 26, "#9A9A9A", Mono);
        Text(dc, "dotnet publish -c Release", 370, 90, 26, "#F2F2F2", Mono);
        string[] lines = ["  Restore complete (0.4s)", "  SweaxClip succeeded (6.1s) → bin\\Release\\publish\\", "", "Build succeeded in 7.2s", "    0 Warning(s)", "    0 Error(s)"];
        for (int i = 0; i < lines.Length; i++)
            Text(dc, lines[i], 30, 150 + i * 44, 26, i == 3 ? "#7CFC9A" : "#CFCFCF", Mono);
        Text(dc, "PS C:\\dev\\sweaxclip>", 30, 450, 26, "#9A9A9A", Mono);
        dc.DrawRectangle(B("#3B8BEB"), null, new Rect(372, 452, 14, 30));
    });

    static BitmapSource Chart() => Draw(1600, 1000, dc =>
    {
        dc.DrawRectangle(B("#FFFFFF"), null, new Rect(0, 0, 1600, 1000));
        Text(dc, "Where copies come from", 100, 80, 52, "#111111", Ui, FontWeights.Bold);
        string[] names = ["Chrome", "VS Code", "Discord", "Explorer", "Word"];
        double[] vals = [0.42, 0.27, 0.14, 0.10, 0.07];
        string[] cols = ["#111111", "#3B8BEB", "#8A8A84", "#C9C9C2", "#E6E6E0"];
        for (int i = 0; i < names.Length; i++)
        {
            dc.DrawRectangle(B(cols[i]), new Pen(B("#111111"), 2), new Rect(100, 220 + i * 140, 1300 * vals[i] / 0.42, 90));
            Text(dc, names[i], 100, 316 + i * 140, 22, "#555555", Mono);
            Text(dc, $"{vals[i] * 100:0}%", 120 + 1300 * vals[i] / 0.42, 238 + i * 140, 40, "#111111", Display);
        }
    });

    static BitmapSource Canvas() => Draw(1800, 1100, dc =>
    {
        dc.DrawRectangle(B("#1E1E1E"), null, new Rect(0, 0, 1800, 1100));
        string[] fills = ["#3B8BEB", "#F4F4F1", "#0A0A0B"];
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect(140 + i * 540, 160, 420, 820);
            dc.DrawRectangle(B(fills[i]), new Pen(B("#3A3A3A"), 2), r);
            Text(dc, $"Frame {i + 1}", r.X, r.Y - 44, 22, "#9A9A9A", Mono);
            string ink = i == 2 ? "#3B8BEB" : "#0A0A0B";
            Text(dc, "SWEAX", r.X + 40, r.Y + 80, 96, ink, Display);
            Text(dc, "STUDIOS", r.X + 40, r.Y + 210, 60, ink, Display);
            dc.DrawRectangle(B(ink), null, new Rect(r.X + 40, r.Bottom - 120, 140, 12));
        }
    });

    static BitmapSource Sunset() => Draw(1600, 1000, dc =>
    {
        var sky = new LinearGradientBrush(new GradientStopCollection
        {
            new((Color)ColorConverter.ConvertFromString("#1B1035"), 0),
            new((Color)ColorConverter.ConvertFromString("#7A2A5A"), 0.45),
            new((Color)ColorConverter.ConvertFromString("#FF7A3D"), 0.78),
            new((Color)ColorConverter.ConvertFromString("#FFD166"), 1),
        }, 90);
        dc.DrawRectangle(sky, null, new Rect(0, 0, 1600, 1000));
        dc.DrawEllipse(B("#FFE9A8"), null, new Point(800, 760), 210, 210);
        for (int i = 0; i < 6; i++)
            dc.DrawRectangle(B("#1B1035"), null, new Rect(0, 760 + i * 44, 1600, 18 + i * 4));
    });

    static BitmapSource Mountains() => Draw(1600, 1000, dc =>
    {
        var sky = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#9FC5E8"), (Color)ColorConverter.ConvertFromString("#F3E6D3"), 90);
        dc.DrawRectangle(sky, null, new Rect(0, 0, 1600, 1000));
        string[] layers = ["#7C93A8", "#566F86", "#33475B", "#1B2633"];
        var rnd = new Random(3);
        for (int l = 0; l < layers.Length; l++)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double baseY = 430 + l * 140;
                c.BeginFigure(new Point(0, 1000), true, true);
                for (int x = 0; x <= 1600; x += 80) c.LineTo(new Point(x, baseY - rnd.NextDouble() * (220 - l * 30)), true, true);
                c.LineTo(new Point(1600, 1000), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(B(layers[l]), null, g);
        }
    });

    static BitmapSource Poster() => Draw(1200, 1500, dc =>
    {
        dc.DrawRectangle(B("#0A0A0B"), null, new Rect(0, 0, 1200, 1500));
        dc.DrawRectangle(B("#3B8BEB"), null, new Rect(80, 80, 1040, 1040));
        Text(dc, "NIGHT", 130, 160, 300, "#0A0A0B", Display);
        Text(dc, "DRIVE", 130, 520, 300, "#0A0A0B", Display);
        Text(dc, "140 BPM · SWEAX", 90, 1200, 54, "#F2F2EE", Mono);
    });
}
