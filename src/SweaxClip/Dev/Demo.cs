using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.Native;
using SweaxClip.UI;

namespace SweaxClip.Dev;

/// <summary>
/// Developer modes that run on a throw-away archive (never the real one):
///   --demo [--lang tr] [--theme light|dark]     browse the library with sample data
///   --shots DIR [--lang tr] [--theme light|dark] render README screenshots
///   --gif DIR [--film panel|search] [--lang tr] [--theme light|dark]   render frames of a scripted session
/// The quick panel has no window frame, so it is filmed exactly as it is: its cards over the desktop.
/// The deck runs on a manual clock while filming, so every frame is one fixed step of its springs.
/// </summary>
public static class Demo
{
    public static async void Run(App app, string[] args)
    {
        string? Arg(string name) { int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Arg("--task-xml") is { } taskFile)
        {
            File.WriteAllText(taskFile, Elevation.TaskXml(args.Contains("admin")), System.Text.Encoding.Unicode);
            app.Shutdown();
            return;
        }
        if (Arg("--classify") is { } samples)
        {
            var blocks = File.ReadAllText(samples).Replace("\r\n", "\n").Split("\n===\n");
            File.WriteAllLines(samples + ".out", blocks.Select(b => $"{Classifier.Classify(b),-6} | {b.Replace("\n", " / ")}"));
            app.Shutdown();
            return;
        }
        bool tr = Arg("--lang") == "tr";
        string? theme = Arg("--theme");
        // --data DIR: a copy of a real archive (bench only); otherwise a fresh synthetic one
        string? data = Arg("--data");
        string dir = data ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sweaxclip-demo-{(tr ? "tr" : "en")}-{theme ?? "auto"}");
        if (data == null) try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Paths.Init(dir);
        var settings = Settings.Load(dir);
        settings.Language = tr ? "tr" : "en";
        Loc.Init(settings.Language);
        app.Settings = settings;
        app.Store = new ClipStore(Paths.Db);
        ThemeManager.Forced = theme == "light" ? false : theme == "dark" ? true : null;
        ThemeManager.PaletteKey = Arg("--palette") ?? "ember";
        settings.PanelStyle = Arg("--style") ?? "timeline";
        ThemeManager.Apply();
        if (data == null) DemoData.Fill(app.Store, tr);

        try
        {
            if (Arg("--bench") is { } bench) await Bench.Run(app, bench, args.Contains("--trim"), args);
            else if (Arg("--shots") is { } shots) await Shots(app, shots);
            else if (Arg("--gif") is { } gif) await Gif(app, gif, Arg("--film") ?? "panel");
            else if (Arg("--appearance") is { } look) await Appearance(app, look);
            else if (Arg("--grab") is { } grabDir) await GrabShot(app, grabDir);
            else
            {
                var lib = new LibraryWindow(app);
                lib.Closed += (_, _) => app.Shutdown();
                lib.Show();
                return;
            }
        }
        catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "demo-error.txt"), ex.ToString()); }
        app.Shutdown();
    }

    static async Task Settle(int ms = 350)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    static bool Dark => ThemeManager.IsDark;
    static Brush Hex(string s) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));

    // ── desktop ─────────────────────────────────────────────────────────────

    static BitmapSource? _wall;

    /// <summary>A quiet abstract wallpaper: soft fields of colour, nothing recognisable.</summary>
    static BitmapSource Wallpaper()
    {
        if (_wall != null) return _wall;
        const int W = 2560, H = 1440;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = Dark
                ? new LinearGradientBrush(Color.FromRgb(0x10, 0x14, 0x19), Color.FromRgb(0x1A, 0x15, 0x1C), 30)
                : new LinearGradientBrush(Color.FromRgb(0xD8, 0xDF, 0xE4), Color.FromRgb(0xE8, 0xE2, 0xDA), 30);
            dc.DrawRectangle(bg, null, new Rect(0, 0, W, H));
            (double x, double y, double r, string c, double a)[] blobs = Dark
                ? [(0.18, 0.28, 0.50, "#1E4A5C", 0.75), (0.80, 0.72, 0.58, "#3B2A4E", 0.70), (0.62, 0.10, 0.36, "#24504A", 0.50), (0.92, 0.16, 0.30, "#5A2E27", 0.45), (0.08, 0.92, 0.38, "#203A5A", 0.55)]
                : [(0.18, 0.28, 0.50, "#A9C4CF", 0.80), (0.80, 0.72, 0.58, "#C9BBD6", 0.70), (0.62, 0.10, 0.36, "#B9D3C4", 0.60), (0.92, 0.16, 0.30, "#EBC7B4", 0.55), (0.08, 0.92, 0.38, "#B4C6DB", 0.60)];
            foreach (var b in blobs)
            {
                var col = (Color)ColorConverter.ConvertFromString(b.c);
                var brush = new RadialGradientBrush(new GradientStopCollection
                {
                    new(Color.FromArgb((byte)(255 * b.a), col.R, col.G, col.B), 0),
                    new(Color.FromArgb((byte)(255 * b.a * 0.45), col.R, col.G, col.B), 0.5),
                    new(Color.FromArgb(0, col.R, col.G, col.B), 1),
                });
                double r = b.r * W;
                dc.DrawEllipse(brush, null, new Point(b.x * W, b.y * H), r, r * 0.75);
            }
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return _wall = rtb;
    }

    /// <summary>Renders the first <paramref name="w"/>×<paramref name="h"/> of a visual, optionally with Windows 11's rounded corners and hairline.</summary>
    static BitmapSource Snap(Visual v, double w, double h, double scale, bool frame)
    {
        var rect = new Rect(0, 0, w, h);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (frame) dc.PushClip(new RectangleGeometry(rect, 8, 8));
            dc.DrawRectangle(new VisualBrush(v)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = rect,
                ViewportUnits = BrushMappingMode.Absolute, Viewport = rect,
            }, null, rect);
            if (frame)
            {
                dc.Pop();
                var edge = Dark ? Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0x00, 0x00, 0x00);
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(edge), 1), new Rect(0.5, 0.5, w - 1, h - 1), 8, 8);
            }
        }
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    static Visual Root(Window w) => (Visual)VisualTreeHelper.GetChild(w, 0);

    /// <summary>Lays images on the wallpaper and crops to <paramref name="view"/> (desktop coordinates).</summary>
    static BitmapSource Scene(Rect view, double scale, Action<Canvas> build)
    {
        var canvas = new Canvas { Width = view.Width, Height = view.Height, ClipToBounds = true };
        var wall = new Image { Source = Wallpaper(), Width = 2560, Height = 1440, Stretch = Stretch.Fill };
        Canvas.SetLeft(wall, -view.X);
        Canvas.SetTop(wall, -view.Y);
        canvas.Children.Add(wall);
        var layer = new Canvas { RenderTransform = new TranslateTransform(-view.X, -view.Y) };
        canvas.Children.Add(layer);
        build(layer);
        canvas.Measure(view.Size);
        canvas.Arrange(new Rect(view.Size));
        canvas.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)(view.Width * scale), (int)(view.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Puts a rendered window on the scene; framed windows get the shadow DWM gives them.</summary>
    static void Put(Canvas c, BitmapSource img, Rect where, bool shadow, double opacity = 1)
    {
        var el = new Image { Source = img, Width = where.Width, Height = where.Height, Stretch = Stretch.Fill, Opacity = opacity };
        if (shadow) el.Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 20, Direction = 270, Opacity = Dark ? 0.55 : 0.26, RenderingBias = RenderingBias.Quality };
        RenderOptions.SetBitmapScalingMode(el, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(el, where.X);
        Canvas.SetTop(el, where.Y);
        c.Children.Add(el);
    }

    static void Save(BitmapSource b, string file)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    static QuickPanel OpenPanel(App app, Point? caret = null, bool hang = false)
    {
        Springs.ManualClock = true;
        var p = new QuickPanel(app) { ShowActivated = false, Left = -6000, Top = 40, Topmost = false };
        p.Show();
        p.PlaceForDemo(caret, hang);
        p.UpdateLayout();
        return p;
    }

    /// <summary>Steps every spring until the panel is still (at most ~4 s of motion).</summary>
    static void Still(Window w)
    {
        for (int i = 0; i < 240; i++)
        {
            bool busy = Springs.Step(1 / 60.0);
            w.UpdateLayout();
            if (!busy) break;
        }
    }

    /// <summary>Loads a category into the panel and lets it come to rest.</summary>
    static async Task Rest(QuickPanel panel, Scope scope, string search, Func<ClipItem, bool>? pick = null)
    {
        var pm = panel.Model;
        pm.Scope = scope;
        pm.Search = search;
        if (panel.FindChild<TextBox>() is { } box) { box.Text = search; box.CaretIndex = search.Length; }
        await pm.Reload(false);
        if (pick != null && pm.Items.FirstOrDefault(pick) is { } hit) pm.Selected = hit;
        else if (pm.Items.Count > 0) pm.Selected = pm.Items[0];
        await Settle(650);
        panel.Enter();
        Still(panel);
        await Settle(120);
        Still(panel);
    }

    // ── screenshots ─────────────────────────────────────────────────────────

    static async Task Shots(App app, string outDir)
    {
        string t = Dark ? "dark" : "light";
        string Out(string name) => System.IO.Path.Combine(outDir, $"{name}-{t}.png");
        bool tr = app.Settings.Language == "tr";

        // library
        var lib = new LibraryWindow(app) { ShowActivated = false, Left = -6000, Top = 40, WindowStartupLocation = WindowStartupLocation.Manual, Width = 1400, Height = 860 };
        lib.Show();
        await Settle(900);
        var lm = lib.Model;
        async Task<BitmapSource> LibSnap(Scope scope, Func<ClipItem, bool>? pick, bool lightbox = false, bool settings = false)
        {
            lib.CloseSettings();
            lib.CloseLightbox();
            lm.Search = "";
            lm.Scope = scope;
            await lm.Reload(false);
            await Settle(300);
            if (pick != null && lm.Items.FirstOrDefault(pick) is { } hit) lm.Selected = hit;
            if (lightbox) await lib.OpenLightbox();
            if (settings)
                typeof(LibraryWindow).GetMethod("Settings_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lib, [lib, new RoutedEventArgs()]);
            await Settle(1300);
            return Snap(Root(lib), lib.ActualWidth, lib.ActualHeight, 2, frame: true);
        }

        // panel
        var panel = OpenPanel(app);
        async Task<BitmapSource> PanelSnap(Scope scope, string search, double scale, Func<ClipItem, bool>? pick = null)
        {
            await Rest(panel, scope, search, pick);
            if (Environment.GetEnvironmentVariable("SWEAXCLIP_HOVER") == "1") ShowHoverActions(panel);
            return Snap(Root(panel), panel.ActualWidth, panel.ActualHeight, scale, frame: false);
        }

        // hero: the library on the desk, the deck dealt open in front of it
        var libRect = new Rect(110, 120, 1400, 860);
        var panRect = new Rect(1330, 400, panel.ActualWidth, panel.ActualHeight);
        var libHero = await LibSnap(Scope.All, null);
        var panHero = await PanelSnap(Scope.All, "", 2);
        Save(Scene(new Rect(40, 50, 1840, 1060), 1.25, c => { Put(c, libHero, libRect, true); Put(c, panHero, panRect, false); }), Out("hero"));

        // panel close-ups
        var solo = new Rect(300, 160, panel.ActualWidth, panel.ActualHeight);
        var view = new Rect(270, 150, 520, panel.ActualHeight + 30);
        foreach (var (scope, name) in new[] { (Scope.All, "panel-all"), (Scope.Images, "panel-images"), (Scope.Links, "panel-links"), (Scope.Code, "panel-code"), (Scope.Colors, "panel-colors") })
        {
            var img = await PanelSnap(scope, "", 2);
            Save(Scene(view, 2, c => Put(c, img, solo, false)), Out(name));
        }
        var found = await PanelSnap(Scope.All, tr ? "fatura" : "invoice", 2);
        Save(Scene(view, 2, c => Put(c, found, solo, false)), Out("panel-search"));
        panel.Close();

        // the desk and the lightbox
        var lr = new Rect(140, 120, 1400, 860);
        var lv = new Rect(70, 60, 1540, 1010);
        async Task Library(string name, Scope scope, Func<ClipItem, bool>? pick, bool lightbox = false, bool settings = false)
        {
            var img = await LibSnap(scope, pick, lightbox, settings);
            Save(Scene(lv, 1.5, c => Put(c, img, lr, true)), Out(name));
        }
        await Library("library-screenshots", Scope.Screenshots, null);
        await Library("library-lightbox", Scope.Screenshots, c => c.Ocr?.Contains("2026-1142") == true, lightbox: true);
        await Library("library-code", Scope.Code, c => c.Text?.Contains("FindAsync") == true, lightbox: true);
        await Library("library-colors", Scope.Colors, null);
        await Library("library-settings", Scope.All, null, settings: true);
        lib.Close();
    }

    /// <summary>Shows the hover actions of every visible row (only for checking how they look).</summary>
    static void ShowHoverActions(QuickPanel panel)
    {
        foreach (var item in panel.Model.Items.Take(8))
        {
            if (panel.FindChild<ListBox>()?.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem row) continue;
            if (Named(row, "acts") is UIElement acts) acts.Opacity = 1;
        }
        panel.UpdateLayout();
    }

    static FrameworkElement? Named(DependencyObject root, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            if (Named(child, name) is { } hit) return hit;
        }
        return null;
    }

    // ── copy text from screen ──────────────────────────────────────────────

    static readonly Brush CodeInk = Hex("#E6E8EE"), CodeKey = Hex("#FF8A65"), CodeStr = Hex("#A5D6A7"), CodeFn = Hex("#80CBC4"), CodeDim = Hex("#6B7280");

    /// <summary>A paused frame of a coding tutorial: the code is only pixels, so it can't be selected.</summary>
    static BitmapSource VideoFrame(bool tr)
    {
        var dv = new DrawingVisual();
        var mono = new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var ui = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        void Run(DrawingContext dc, string text, Brush b, ref double x, double y, Typeface f, double size)
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, f, size, b, 1);
            dc.DrawText(ft, new Point(x, y));
            x += ft.WidthIncludingTrailingWhitespace;
        }
        (string, Brush)[][] lines =
        [
            [(tr ? "# 04 — fonksiyonlar" : "# 04 — functions", CodeDim)],
            [("def ", CodeKey), ("greet", CodeFn), ("(name):", CodeInk)],
            [("    message = ", CodeInk), ("f\"Merhaba, {name}!\"", CodeStr)],
            [("    print", CodeFn), ("(message)", CodeInk)],
            [("    ", CodeInk), ("return ", CodeKey), ("message", CodeInk)],
            [("", CodeInk)],
            [("greet", CodeFn), ("(", CodeInk), ("\"Dünya\"", CodeStr), (")", CodeInk)],
        ];
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Hex("#1E1F24"), null, new Rect(0, 0, 1280, 720));
            dc.DrawRectangle(Hex("#18191D"), null, new Rect(0, 0, 1280, 48));
            dc.DrawRectangle(Hex("#1E1F24"), null, new Rect(24, 8, 150, 40));
            double tx = 44;
            Run(dc, "app.py", CodeInk, ref tx, 17, ui, 16);
            for (int i = 0; i < lines.Length; i++)
            {
                double y = 104 + i * 46, x = 64;
                Run(dc, (i + 1).ToString(), CodeDim, ref x, y, mono, 26);
                x = 120;
                foreach (var (text, brush) in lines[i]) Run(dc, text, brush, ref x, y, mono, 26);
            }
        }
        var rtb = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>A paused frame of a talk: a slide whose words are only pixels.</summary>
    static BitmapSource SlideFrame(bool tr)
    {
        var dv = new DrawingVisual();
        var head = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var body = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        void Text(DrawingContext dc, string text, double x, double y, Typeface f, double size, Brush b) =>
            dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, f, size, b, 1), new Point(x, y));
        string[] items = tr
            ? ["1. Sürümü yükselt ve derlemeyi etiketle", "2. Zip dosyasını ve lite exe'yi ekle", "3. Bağlantıyı #studio kanalında paylaş", "4. Değişiklik notlarını İngilizce ve Türkçe yaz"]
            : ["1. Bump the version and tag the build", "2. Attach the zip and the lite exe", "3. Share the link in the #studio channel", "4. Write the release notes in English and Turkish"];
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#1B1D2A"), (Color)ColorConverter.ConvertFromString("#121318"), 90), null, new Rect(0, 0, 1280, 720));
            dc.DrawRectangle(Hex("#E4572E"), null, new Rect(96, 120, 64, 6));
            Text(dc, tr ? "Bir sürümü yayınlamak" : "Shipping a release", 96, 150, head, 56, Hex("#F2F2F5"));
            for (int i = 0; i < items.Length; i++) Text(dc, items[i], 104, 270 + i * 56, body, 32, Hex("#C9CCD8"));
            Text(dc, "Sweax Studios · 2026", 96, 640, body, 18, Hex("#6B6F82"));
        }
        var rtb = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>A plain video player window around the frame.</summary>
    static FrameworkElement Player(BitmapSource frame, bool tr)
    {
        var title = new TextBlock { Text = tr ? "Sürüm günü — sunum.mp4" : "Release day — talk.mp4", Foreground = Hex("#C9CCD3"), FontSize = 13, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        var top = new Grid { Height = 44, Background = Hex("#0E0F12"), Children = { title } };
        var image = new Image { Source = frame, Width = 1280, Height = 720, Stretch = Stretch.None };
        var bar = new Grid { Height = 56, Background = Hex("#0E0F12") };
        bar.Children.Add(new TextBlock { Text = "\uE769", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 18, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) });
        bar.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Hex("#3A3D45"), Margin = new Thickness(64, 0, 150, 0) });
        bar.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Hex("#E4572E"), Margin = new Thickness(64, 0, 0, 0), Width = 440, HorizontalAlignment = HorizontalAlignment.Left });
        bar.Children.Add(new TextBlock { Text = "04:12 / 10:00", Foreground = Hex("#C9CCD3"), FontSize = 13, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 22, 0) });
        var dock = new DockPanel { Background = Hex("#0B0C0E"), Width = 1320, Height = 840 };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(bar);
        dock.Children.Add(image);
        dock.Measure(new Size(1320, 840));
        dock.Arrange(new Rect(0, 0, 1320, 840));
        dock.UpdateLayout();
        return dock;
    }

    /// <summary>Renders grab-{theme}.png and checks the OCR on code, small interface text and Turkish letters.</summary>
    static async Task GrabShot(App app, string outDir)
    {
        Directory.CreateDirectory(outDir);
        string t = Dark ? "dark" : "light";
        bool tr = app.Settings.Language == "tr";
        const double W = 1600, H = 1000;
        var playerRect = new Rect(140, 70, 1320, 840);
        var player = Player(SlideFrame(tr), tr);
        var desk = Scene(new Rect(0, 0, W, H), 1, c => Put(c, Demo.Snap(player, 1320, 840, 1, frame: true), playerRect, true));

        var o = new GrabOverlay(new Win32.RECT { Left = 0, Top = 0, Right = (int)W, Bottom = (int)H }, desk)
        {
            Offscreen = true, ShowActivated = false, Left = -32000, Top = -32000, Width = W, Height = H,
        };
        o.Show();
        o.UpdateLayout();
        await Settle(300);
        // the slide's list: the frame sits at (160, 114) on the desktop
        var sel = new Rect(160 + 96, 114 + 250, 820, 250);
        var crop = o.Select(sel);
        o.UpdateLayout();
        string text = (await OcrService.ReadScreenAsync(crop)).Trim();

        var shot = Demo.Snap(Root(o), W, H, 1, frame: false);
        var chip = GrabToast.Chip(text);
        Save(Scene(new Rect(0, 0, W, H), 1.25, c =>
        {
            Put(c, shot, new Rect(0, 0, W, H), false);
            Canvas.SetLeft(chip, sel.Left - 16);
            Canvas.SetTop(chip, sel.Bottom + 4);
            c.Children.Add(chip);
        }), System.IO.Path.Combine(outDir, $"grab-{t}.png"));
        o.Close();

        // small interface text and Turkish letters, the way they appear on screen
        var report = new System.Text.StringBuilder();
        report.AppendLine("== code ==").AppendLine(text);
        var code = VideoFrame(tr);
        var codeCrop = new CroppedBitmap(code, new Int32Rect(100, 140, 600, 290));
        report.AppendLine("== code from a video ==").AppendLine((await OcrService.ReadScreenAsync(codeCrop)).Trim());
        foreach (var (sample, size) in new[] { ("Error 0x80070005: Access is denied.", 13.0), ("Kopyaladığın her şey saklı — Şişli, İstanbul", 14.0), ("Toplam: 4.250,00 ₺ · Son ödeme 15 Kasım 2026", 12.0) })
        {
            var tb = new TextBlock { Text = sample, FontSize = size, FontFamily = new FontFamily("Segoe UI"), Foreground = Hex("#1C1C1C"), Background = Hex("#FFFFFF"), Padding = new Thickness(6, 3, 6, 4) };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tb.Arrange(new Rect(tb.DesiredSize));
            var img = new RenderTargetBitmap((int)Math.Ceiling(tb.ActualWidth), (int)Math.Ceiling(tb.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            img.Render(tb);
            img.Freeze();
            report.AppendLine($"== {size}px ==").AppendLine(sample).AppendLine((await OcrService.ReadScreenAsync(img)).Trim());
        }
        File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sweaxclip-grab-ocr-{(tr ? "tr" : "en")}-{t}.txt"), report.ToString());
    }

    // ── appearance: every palette, and the deck style ──────────────────────

    /// <summary>Renders palettes-{theme}.png (the timeline in every palette) and panel-deck-{theme}.png.</summary>
    static async Task Appearance(App app, string outDir)
    {
        string t = Dark ? "dark" : "light";
        var shots = new List<BitmapSource>();
        double pw = 0, ph = 0;
        foreach (var palette in Palettes.All)
        {
            ThemeManager.PaletteKey = palette.Key;
            ThemeManager.Apply();
            var panel = OpenPanel(app);
            await Rest(panel, Scope.All, "");
            pw = panel.ActualWidth; ph = panel.ActualHeight;
            shots.Add(Snap(Root(panel), pw, ph, 1.5, frame: false));
            panel.Close();
        }
        // seven panels side by side on the desktop, slightly overlapping like a fan of swatches
        double step = pw * 0.62;
        var view = new Rect(0, 0, step * (shots.Count - 1) + pw + 40, ph + 20);
        Save(Scene(view, 1.25, c =>
        {
            for (int i = 0; i < shots.Count; i++) Put(c, shots[i], new Rect(20 + i * step, 10, pw, ph), false);
        }), System.IO.Path.Combine(outDir, $"palettes-{t}.png"));

        // the deck style, in the default palette
        ThemeManager.PaletteKey = "ember";
        ThemeManager.Apply();
        var deck = new DeckPanel(app) { ShowActivated = false, Left = -6000, Top = 40, Topmost = false };
        deck.DeckView.ManualClock = true;
        deck.Show();
        deck.UpdateLayout();
        await deck.Model.Reload(false);
        deck.Model.Selected = deck.Model.Items[0];
        deck.lid.Opacity = 1;
        deck.hints.Opacity = 1;
        await Settle(700);
        deck.DeckView.Sync(Deck.Mode.Instant);
        deck.UpdateLayout();
        await Settle(200);
        var img = Snap(Root(deck), deck.ActualWidth, deck.ActualHeight, 2, frame: false);
        var dv = new Rect(270, 150, deck.ActualWidth + 60, deck.ActualHeight + 30);
        Save(Scene(dv, 2, c => Put(c, img, new Rect(300, 160, deck.ActualWidth, deck.ActualHeight), false)), System.IO.Path.Combine(outDir, $"panel-deck-{t}.png"));
        deck.Close();
    }

    // ── the Win+V film ──────────────────────────────────────────────────────

    /// <summary>A plain text editor for the deck to open over; the caret is where the clip will land.</summary>
    sealed class Editor
    {
        public readonly Border Window;
        public readonly Run Pasted = new();
        public readonly Rectangle Caret;

        public Editor(bool tr)
        {
            var ink = Hex(Dark ? "#E6E6E6" : "#1C1C1C");
            var dim = Hex(Dark ? "#9A9A9A" : "#6B6B6B");
            var text = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 15, Foreground = ink, LineHeight = 25, TextWrapping = TextWrapping.Wrap };
            string[] lines = tr
                ? ["Sürüm kontrol listesi — v1.2", "", "1. Sürümü yükselt ve derlemeyi etiketle.", "2. Zip'i ve lite exe'yi ekle.", "3. Bağlantıyı #studio kanalına at:", ""]
                : ["Release checklist — v1.2", "", "1. Bump the version and tag the build.", "2. Attach the zip and the lite exe.", "3. Post the link in #studio:", ""];
            foreach (var l in lines)
            {
                text.Inlines.Add(new Run(l) { FontWeight = l == lines[0] ? FontWeights.SemiBold : FontWeights.Normal });
                text.Inlines.Add(new LineBreak());
            }
            text.Inlines.Add(new Run(tr ? "İndir → " : "Download → "));
            Pasted.Foreground = ink;
            text.Inlines.Add(Pasted);
            Caret = new Rectangle { Width = 1.5, Height = 19, Fill = ink };
            text.Inlines.Add(new InlineUIContainer(Caret) { BaselineAlignment = BaselineAlignment.Center });

            var bar = new Grid { Height = 40, Background = Hex(Dark ? "#202020" : "#F3F3F3") };
            bar.Children.Add(new TextBlock { Text = tr ? "yayin-notlari.txt — Not Defteri" : "release-notes.txt — Notepad", FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
            bar.Children.Add(new TextBlock { Text = "          ", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 10, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 18, 0) });
            var body = new DockPanel { Background = Hex(Dark ? "#272727" : "#FCFCFC") };
            DockPanel.SetDock(bar, Dock.Top);
            body.Children.Add(bar);
            body.Children.Add(new Border { Padding = new Thickness(22, 18, 22, 18), Child = text });
            Window = new Border { Width = 640, Height = 330, Child = body };
            Window.Measure(new Size(640, 330));
            Window.Arrange(new Rect(0, 0, 640, 330));
            Window.UpdateLayout();
        }

        public Point CaretAt => Caret.TranslatePoint(new Point(0, 0), Window);
        public BitmapSource Snap() { Window.UpdateLayout(); return Demo.Snap(Window, 640, 330, 1, frame: true); }
    }

    /// <summary>The key press, drawn as a small cap that floats in while the shortcut is held.</summary>
    static Border Keys(string text) => new()
    {
        Background = Hex(Dark ? "#F2EFE9" : "#23201D"),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 8, 14, 9),
        Child = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Hex(Dark ? "#23201D" : "#F2EFE9") },
    };

    /// <summary>Frames of a scripted session (25 fps); stitched into a GIF with ffmpeg.</summary>
    static async Task Gif(App app, string outDir, string which)
    {
        Directory.CreateDirectory(outDir);
        bool tr = app.Settings.Language == "tr";
        const double dt = 0.04;

        var editor = new Editor(tr);
        var editorRect = new Rect(70, 40, 640, 330);
        var caret = editorRect.TopLeft + (Vector)editor.CaretAt;
        bool film = which == "panel";

        // the panel opens under the caret, exactly as QuickPanel.Placement does
        var probe = OpenPanel(app);
        double pw = probe.ActualWidth, ph = probe.ActualHeight;
        probe.Close();
        double wx = caret.X - 22 - 18, wy = caret.Y + editor.Caret.Height + 6 - 20;
        var panelRect = film ? new Rect(wx, wy, pw, ph) : new Rect(40, 20, pw, ph);
        var view = film ? new Rect(40, 10, 700, 860) : new Rect(20, 0, pw + 40, ph + 30);
        var panel = OpenPanel(app, film ? new Point(caret.X - wx, caret.Y + editor.Caret.Height - wy) : null, hang: film);
        var pm = panel.Model;

        await pm.Reload(false);
        pm.Selected = pm.Items[0];
        await Settle(800);

        int n = 0, tick = 0;
        bool panelShown = !film;
        double keysOpacity = 0;
        string keyText = "⊞ Win  +  V";
        void Frame(int repeat = 1)
        {
            for (int r = 0; r < repeat; r++)
            {
                tick++;
                editor.Caret.Opacity = !panelShown && (tick / 13) % 2 == 0 ? 1 : 0;
                var ed = film ? editor.Snap() : null;
                var pan = panelShown ? Snap(Root(panel), panelRect.Width, panelRect.Height, 1, frame: false) : null;
                var img = Scene(view, 1, c =>
                {
                    if (ed != null) Put(c, ed, editorRect, true);
                    if (pan != null) Put(c, pan, panelRect, false);
                    if (keysOpacity > 0)
                    {
                        var k = Keys(keyText);
                        k.Opacity = keysOpacity;
                        Canvas.SetLeft(k, view.Right - 168);
                        Canvas.SetTop(k, view.Bottom - 56);
                        c.Children.Add(k);
                    }
                });
                Save(img, System.IO.Path.Combine(outDir, $"f{n++:0000}.png"));
            }
        }
        void Run(int frames, Action<int>? each = null)
        {
            for (int i = 0; i < frames; i++)
            {
                each?.Invoke(i);
                Springs.Step(dt);
                panel.UpdateLayout();
                Frame();
            }
        }

        if (film)
        {
            // typing; then Win+V
            Frame(12);
            for (int i = 1; i <= 4; i++) { keysOpacity = i / 4.0; Frame(); }
            panelShown = true;
            panel.Enter();
            Run(26, i => keysOpacity = Math.Max(0, 1 - i / 6.0));
            Frame(6);

            // ↓ walks back through history: each row opens as it is reached
            foreach (int step in new[] { 1, 1, 1, -1 })
            {
                pm.Move(step);
                Run(14);
            }
            Frame(4);

            // Tab through the categories, ending on links
            foreach (var s in new[] { Scope.Images, Scope.Screenshots, Scope.Code, Scope.Colors, Scope.Links })
            {
                pm.Scope = s;
                await pm.Reload(false);
                if (pm.Items.Count > 0) pm.Selected = pm.Items[0];
                await Settle(380);
                panel.Stagger();
                Run(s is Scope.Images or Scope.Screenshots ? 20 : 16);
            }
            Frame(6);

            // Enter: the panel folds into the caret and the link lands in the text
            var link = pm.Selected!;
            keyText = "Enter";
            keysOpacity = 1;
            _ = panel.Leave();
            Run(5);
            panelShown = false;
            editor.Pasted.Text = link.Text;
            for (int i = 0; i < 40; i++)
            {
                keysOpacity = Math.Max(0, 1 - i / 6.0);
                Frame();
            }
        }
        else
        {
            // search: a word typed in finds the screenshot it appears in
            panel.Enter();
            Run(24);
            Frame(10);
            string word = tr ? "fatura" : "invoice";
            var box = panel.FindChild<TextBox>();
            for (int i = 1; i <= word.Length; i++)
            {
                pm.Search = word[..i];
                if (box != null) { box.Text = word[..i]; box.CaretIndex = i; }
                await pm.Reload(false);
                if (pm.Items.Count > 0) pm.Selected = pm.Items[0];
                await Settle(i == word.Length ? 500 : 120);
                Run(i == word.Length ? 34 : 4);
            }
            Frame(30);
            pm.Search = "";
            if (box != null) box.Text = "";
            await pm.Reload(false);
            pm.Selected = pm.Items[0];
            await Settle(300);
            Run(26);
            Frame(8);
        }
        panel.Close();
    }
}
