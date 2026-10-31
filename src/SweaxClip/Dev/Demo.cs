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
using SweaxClip.UI;

namespace SweaxClip.Dev;

/// <summary>
/// Developer modes that run on a throw-away archive (never the real one):
///   --demo [--lang tr] [--theme light|dark]     browse the library with sample data
///   --shots DIR [--lang tr] [--theme light|dark] render README screenshots
///   --gif DIR [--lang tr] [--theme light|dark]   render frames of a scripted Win+V session
/// The quick panel has no window frame, so it is filmed exactly as it is: its cards over the desktop.
/// The deck runs on a manual clock while filming, so every frame is one fixed step of its springs.
/// </summary>
public static class Demo
{
    public static async void Run(App app, string[] args)
    {
        string? Arg(string name) { int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        bool tr = Arg("--lang") == "tr";
        string? theme = Arg("--theme");
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sweaxclip-demo-{(tr ? "tr" : "en")}-{theme ?? "auto"}");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Paths.Init(dir);
        var settings = Settings.Load(dir);
        settings.Language = tr ? "tr" : "en";
        Loc.Init(settings.Language);
        app.Settings = settings;
        app.Store = new ClipStore(Paths.Db);
        ThemeManager.Forced = theme == "light" ? false : theme == "dark" ? true : null;
        ThemeManager.Apply();
        DemoData.Fill(app.Store, tr);

        try
        {
            if (Arg("--shots") is { } shots) await Shots(app, shots);
            else if (Arg("--gif") is { } gif) await Gif(app, gif);
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

    static QuickPanel OpenPanel(App app)
    {
        var p = new QuickPanel(app) { ShowActivated = false, Left = -6000, Top = 40, Topmost = false };
        p.DeckView.ManualClock = true;
        p.Show();
        p.UpdateLayout();
        return p;
    }

    /// <summary>Loads a category into the panel and lays its deck out at rest.</summary>
    static async Task Rest(QuickPanel panel, Scope scope, string search)
    {
        var pm = panel.Model;
        pm.Search = search;
        pm.Scope = scope;
        await pm.Reload(false);
        if (pm.Items.Count > 0) pm.Selected = pm.Items[0];
        if (panel.FindChild<TextBox>() is { } box) box.Text = search;
        panel.lid.Opacity = 1;
        panel.hints.Opacity = 1;
        await Settle(600);
        panel.DeckView.Sync(Deck.Mode.Instant);
        panel.UpdateLayout();
        await Settle(150);
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
        async Task<BitmapSource> PanelSnap(Scope scope, string search, double scale)
        {
            await Rest(panel, scope, search);
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
    static async Task Gif(App app, string outDir)
    {
        Directory.CreateDirectory(outDir);
        bool tr = app.Settings.Language == "tr";
        const double dt = 0.04;

        var editor = new Editor(tr);
        var editorRect = new Rect(300, 480, 640, 330);
        var caret = editorRect.TopLeft + (Vector)editor.CaretAt;
        var view = new Rect(270, 0, 700, 840);

        var panel = OpenPanel(app);
        var pm = panel.Model;
        var deck = panel.DeckView;
        // same rule as QuickPanel.Placement when the caret is low on the screen: the deck rises from it
        var panelRect = new Rect(caret.X - 38, Math.Max(-16, caret.Y - panel.ActualHeight + 14), panel.ActualWidth, panel.ActualHeight);

        await pm.Reload(false);
        pm.Selected = pm.Items[0];
        await Settle(800);

        int n = 0, tick = 0;
        bool panelShown = false;
        double keysOpacity = 0;
        string keyText = "⊞ Win  +  V";
        void Frame(int repeat = 1)
        {
            for (int r = 0; r < repeat; r++)
            {
                tick++;
                editor.Caret.Opacity = !panelShown && (tick / 13) % 2 == 0 ? 1 : 0;
                var ed = editor.Snap();
                var pan = panelShown ? Snap(Root(panel), panelRect.Width, panelRect.Height, 1, frame: false) : null;
                var img = Scene(view, 1, c =>
                {
                    Put(c, ed, editorRect, true);
                    if (pan != null) Put(c, pan, panelRect, false);
                    if (keysOpacity > 0)
                    {
                        var k = Keys(keyText);
                        k.Opacity = keysOpacity;
                        Canvas.SetLeft(k, view.Right - 168);
                        Canvas.SetTop(k, 26);
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
                deck.StepFrame(dt);
                Frame();
            }
        }
        static double EaseBack(double t) { const double s = 1.4; t -= 1; return t * t * ((s + 1) * t + s) + 1; }

        // the user is typing; then presses Win+V
        Frame(14);
        for (int i = 1; i <= 4; i++) { keysOpacity = i / 4.0; Frame(); }

        // the lid drops in and the deck is dealt from the caret upward
        deck.Clear();
        panelShown = true;
        panel.lid.Opacity = 0;
        panel.hints.Opacity = 0;
        deck.Sync(Deck.Mode.Deal);
        Run(28, i =>
        {
            double t = Math.Min(1, (i + 1) / 9.0);
            panel.lid.Opacity = Math.Min(1, (i + 1) / 4.0);
            panel.lidShift.Y = -14 * (1 - EaseBack(t));
            panel.hints.Opacity = Math.Clamp((i - 5) / 6.0, 0, 1);
            keysOpacity = Math.Max(0, 1 - i / 6.0);
        });
        Frame(8);

        // ↑ walks back through history, ↓ comes forward
        foreach (int step in new[] { 1, 1, 1, -1, -1, -1 })
        {
            pm.Move(step);
            Run(step > 0 ? 9 : 7);
        }
        Frame(4);

        // Tab through the categories, ending on links
        foreach (var s in new[] { Scope.Images, Scope.Screenshots, Scope.Code, Scope.Colors, Scope.Links })
        {
            pm.Scope = s;
            await pm.Reload(false);
            if (pm.Items.Count > 0) pm.Selected = pm.Items[0];
            await Settle(350);
            Run(s is Scope.Images or Scope.Screenshots ? 18 : 15);
        }
        Frame(8);

        // Enter: the card is thrown at the caret and lands as text
        var link = pm.Selected!;
        keyText = "Enter";
        keysOpacity = 1;
        var deckAt = deck.TranslatePoint(new Point(0, 0), panel);
        deck.Fling(new Point(caret.X - panelRect.X - deckAt.X, caret.Y - panelRect.Y - deckAt.Y));
        Run(5, i =>
        {
            panel.lid.Opacity = Math.Max(0, 1 - (i + 1) / 4.0);
            panel.hints.Opacity = panel.lid.Opacity;
        });
        panelShown = false;
        editor.Pasted.Text = link.Text;
        for (int i = 0; i < 40; i++)
        {
            keysOpacity = Math.Max(0, 1 - i / 6.0);
            Frame();
        }
        panel.Close();
    }
}
