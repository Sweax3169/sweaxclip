using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.UI;

namespace SweaxClip.Dev;

/// <summary>
/// Developer modes that run on a throw-away archive (never the real one):
///   --demo [--lang tr] [--theme light|dark]     browse the library with sample data
///   --shots DIR [--lang tr] [--theme light|dark] render README screenshots
///   --gif DIR [--lang tr] [--theme light|dark]   render frames of a scripted quick-panel session
/// Acrylic and Mica are drawn by DWM and never reach a RenderTargetBitmap, so the shots recreate
/// them: a desktop wallpaper, blurred under each window and tinted with the Windows 11 backdrop tints.
/// </summary>
public static class Demo
{
    public static async void Run(App app, string[] args)
    {
        string? Arg(string name) { int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        bool tr = Arg("--lang") == "tr";
        string? theme = Arg("--theme");
        string dir = Path.Combine(Path.GetTempPath(), $"sweaxclip-demo-{(tr ? "tr" : "en")}-{theme ?? "auto"}");
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
        catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "demo-error.txt"), ex.ToString()); }
        app.Shutdown();
    }

    static async Task Settle(int ms = 350)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    static bool Dark => ThemeManager.IsDark;

    // ── desktop + backdrop simulation ───────────────────────────────────────

    static BitmapSource? _wall;

    /// <summary>A calm, abstract desktop wallpaper (soft colour fields, no recognisable artwork).</summary>
    static BitmapSource Wallpaper()
    {
        if (_wall != null) return _wall;
        const int W = 2560, H = 1440;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = Dark
                ? new LinearGradientBrush(Color.FromRgb(0x0A, 0x12, 0x24), Color.FromRgb(0x16, 0x0E, 0x2C), 35)
                : new LinearGradientBrush(Color.FromRgb(0xD9, 0xE6, 0xF7), Color.FromRgb(0xEC, 0xE3, 0xF6), 35);
            dc.DrawRectangle(bg, null, new Rect(0, 0, W, H));
            (double x, double y, double r, string c, double a)[] blobs = Dark
                ? [(0.22, 0.30, 0.55, "#2C6BD6", 0.70), (0.78, 0.70, 0.60, "#6A35C8", 0.62), (0.58, 0.12, 0.40, "#1294B8", 0.42), (0.90, 0.10, 0.35, "#C2457F", 0.30), (0.10, 0.90, 0.40, "#1C4FA0", 0.45)]
                : [(0.22, 0.30, 0.55, "#7FAEF0", 0.75), (0.78, 0.70, 0.60, "#BFA0EE", 0.70), (0.58, 0.12, 0.40, "#8ED8E6", 0.55), (0.90, 0.10, 0.35, "#F4B9D2", 0.50), (0.10, 0.90, 0.40, "#9CC2F5", 0.55)];
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

    /// <summary>Blurred wallpaper under a window rect, at the window's render scale.</summary>
    static BitmapSource Blurred(Rect where, double radius, double scale)
    {
        var wall = Wallpaper();
        double pad = radius * 2;
        var src = new Int32Rect((int)Math.Max(0, where.X - pad), (int)Math.Max(0, where.Y - pad), 0, 0);
        src.Width = (int)Math.Min(wall.PixelWidth - src.X, where.Width + pad * 2);
        src.Height = (int)Math.Min(wall.PixelHeight - src.Y, where.Height + pad * 2);
        var img = new Image
        {
            Source = new CroppedBitmap(wall, src),
            Width = src.Width,
            Height = src.Height,
            Stretch = Stretch.Fill,
            Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        };
        var host = new Canvas { Width = where.Width, Height = where.Height, ClipToBounds = true };
        Canvas.SetLeft(img, src.X - where.X);
        Canvas.SetTop(img, src.Y - where.Y);
        host.Children.Add(img);
        host.Measure(new Size(where.Width, where.Height));
        host.Arrange(new Rect(0, 0, where.Width, where.Height));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)(where.Width * scale), (int)(where.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(host);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Renders a window's content over its simulated backdrop, with the rounded corners and hairline DWM draws.</summary>
    static BitmapSource Snapshot(FrameworkElement content, Rect onDesktop, bool acrylic, double scale)
    {
        content.UpdateLayout();
        double w = content.ActualWidth, h = content.ActualHeight;
        var back = Blurred(onDesktop with { Width = w, Height = h }, acrylic ? 60 : 160, scale);
        Color tint = acrylic
            ? (Dark ? Color.FromArgb(0xE0, 0x2C, 0x2C, 0x2C) : Color.FromArgb(0xD4, 0xFC, 0xFC, 0xFC))
            : (Dark ? Color.FromArgb(0xEC, 0x20, 0x20, 0x20) : Color.FromArgb(0xE0, 0xF3, 0xF3, 0xF3));
        var border = Dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00);
        var rect = new Rect(0, 0, w, h);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(rect, 8, 8));
            dc.DrawImage(back, rect);
            dc.DrawRectangle(new SolidColorBrush(tint), null, rect);
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, rect);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(border), 1), new Rect(0.5, 0.5, w - 1, h - 1), 8, 8);
        }
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    record Placed(BitmapSource Image, Rect Where);

    /// <summary>Lays snapshots on the wallpaper with Windows-like shadows and crops to <paramref name="view"/>.</summary>
    static BitmapSource Scene(Rect view, double scale, params Placed[] windows)
    {
        var canvas = new Canvas { Width = view.Width, Height = view.Height, ClipToBounds = true };
        var wall = new Image { Source = Wallpaper(), Width = 2560, Height = 1440, Stretch = Stretch.Fill };
        Canvas.SetLeft(wall, -view.X);
        Canvas.SetTop(wall, -view.Y);
        canvas.Children.Add(wall);
        foreach (var p in windows)
        {
            var img = new Image
            {
                Source = p.Image,
                Width = p.Where.Width,
                Height = p.Where.Height,
                Stretch = Stretch.Fill,
                Effect = new DropShadowEffect { BlurRadius = 64, ShadowDepth = 22, Direction = 270, Opacity = Dark ? 0.55 : 0.28, RenderingBias = RenderingBias.Quality },
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(img, p.Where.X - view.X);
            Canvas.SetTop(img, p.Where.Y - view.Y);
            canvas.Children.Add(img);
        }
        canvas.Measure(view.Size);
        canvas.Arrange(new Rect(view.Size));
        canvas.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)(view.Width * scale), (int)(view.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        rtb.Freeze();
        return rtb;
    }

    static void Save(BitmapSource b, string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    static QuickPanel OpenPanel(App app)
    {
        var p = new QuickPanel(app) { ShowActivated = false, Left = -6000, Top = 40, Topmost = false };
        p.Show();
        ((FrameworkElement)p.Content).Opacity = 1;
        return p;
    }

    // ── screenshots ─────────────────────────────────────────────────────────

    static async Task Shots(App app, string outDir)
    {
        string t = Dark ? "dark" : "light";

        // quick panel
        var panel = OpenPanel(app);
        var pm = panel.Model;
        var proot = (FrameworkElement)panel.Content;
        async Task<BitmapSource> PanelSnap(Scope scope, string search, Rect where, double scale)
        {
            pm.Search = search;
            pm.Scope = scope;
            await pm.Reload(false);
            if (pm.Items.Count > 0) pm.Selected = pm.Items[0];
            if (proot.FindChild<TextBox>() is { } box) box.Text = search;
            await Settle(700);
            return Snapshot(proot, where, acrylic: true, scale);
        }

        // library
        var lib = new LibraryWindow(app) { ShowActivated = false, Left = -6000, Top = 40, WindowStartupLocation = WindowStartupLocation.Manual, Width = 1400, Height = 860 };
        lib.Show();
        await Settle(900);
        var lm = lib.Model;
        var lroot = (FrameworkElement)lib.Content;
        async Task<BitmapSource> LibSnap(Scope scope, Func<ClipItem, bool>? pick, Rect where, double scale, bool settings = false)
        {
            lib.CloseSettings();
            lm.GridPreferred = true;
            lm.Search = "";
            lm.Scope = scope;
            await lm.Reload(false);
            if (pick != null && lm.Items.FirstOrDefault(pick) is { } hit) lm.Selected = hit;
            if (settings)
                typeof(LibraryWindow).GetMethod("Settings_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lib, [lib, new RoutedEventArgs()]);
            await Settle(1300);
            return Snapshot(lroot, where, acrylic: false, scale);
        }

        // hero: library with the panel opened over it
        var libRect = new Rect(110, 120, 1400, 860);
        var panRect = new Rect(1440, 470, 384, 540);
        var libHero = await LibSnap(Scope.All, c => c.Kind == ClipKind.Screenshot, libRect, 2);
        var panHero = await PanelSnap(Scope.All, "", panRect, 2);
        Save(Scene(new Rect(30, 40, 1880, 1060), 1.25, new Placed(libHero, libRect), new Placed(panHero, panRect)), Path.Combine(outDir, $"hero-{t}.png"));

        // panel close-ups
        var solo = new Rect(260, 200, 384, 540);
        var view = new Rect(170, 130, 564, 700);
        Save(Scene(view, 2, new Placed(await PanelSnap(Scope.All, "", solo, 2), solo)), Path.Combine(outDir, $"panel-all-{t}.png"));
        Save(Scene(view, 2, new Placed(await PanelSnap(Scope.Images, "", solo, 2), solo)), Path.Combine(outDir, $"panel-images-{t}.png"));
        Save(Scene(view, 2, new Placed(await PanelSnap(Scope.Links, "", solo, 2), solo)), Path.Combine(outDir, $"panel-links-{t}.png"));
        Save(Scene(view, 2, new Placed(await PanelSnap(Scope.Code, "", solo, 2), solo)), Path.Combine(outDir, $"panel-code-{t}.png"));
        Save(Scene(view, 2, new Placed(await PanelSnap(Scope.Colors, "", solo, 2), solo)), Path.Combine(outDir, $"panel-colors-{t}.png"));
        Save(Scene(view, 2, new Placed(await PanelSnap(Scope.All, app.Settings.Language == "tr" ? "fatura" : "invoice", solo, 2), solo)), Path.Combine(outDir, $"panel-search-{t}.png"));
        panel.Close();

        // library views
        var lr = new Rect(140, 120, 1400, 860);
        var lv = new Rect(70, 60, 1540, 1010);
        Save(Scene(lv, 1.5, new Placed(await LibSnap(Scope.Screenshots, c => c.Width == 1400, lr, 2), lr)), Path.Combine(outDir, $"library-screenshots-{t}.png"));
        Save(Scene(lv, 1.5, new Placed(await LibSnap(Scope.Code, c => c.Text?.Contains("FindAsync") == true, lr, 2), lr)), Path.Combine(outDir, $"library-code-{t}.png"));
        Save(Scene(lv, 1.5, new Placed(await LibSnap(Scope.Colors, c => c.Text == "#0F6CBD", lr, 2), lr)), Path.Combine(outDir, $"library-colors-{t}.png"));
        Save(Scene(lv, 1.5, new Placed(await LibSnap(Scope.All, null, lr, 2, settings: true), lr)), Path.Combine(outDir, $"library-settings-{t}.png"));
        lib.Close();
    }

    /// <summary>Frames of a scripted quick-panel session; stitched into a GIF with ffmpeg.</summary>
    static async Task Gif(App app, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var panel = OpenPanel(app);
        var pm = panel.Model;
        var root = (FrameworkElement)panel.Content;
        var solo = new Rect(220, 170, 384, 540);
        var view = new Rect(160, 110, 504, 660);
        int n = 0;
        const double scale = 1.25;
        void Frame(int repeat = 1, double slide = 0, double opacity = 1)
        {
            root.Opacity = opacity;
            var snap = Snapshot(root, solo, acrylic: true, scale);
            string first = Path.Combine(outDir, $"f{n:0000}.png");
            Save(Scene(view, scale, new Placed(snap, solo with { Y = solo.Y + slide })), first);
            n++;
            for (int i = 1; i < repeat; i++) File.Copy(first, Path.Combine(outDir, $"f{n++:0000}.png"));
        }

        await pm.Reload(false);
        pm.Selected = pm.Items[0];
        await Settle(900);

        // flyout entrance
        for (int i = 1; i <= 4; i++)
        {
            double t = i / 4.0, e = 1 - Math.Pow(1 - t, 4);
            Frame(1, 28 * (1 - e), Math.Min(1, t * 2.4));
        }
        Frame(12);

        for (int i = 0; i < 3; i++)
        {
            pm.Move(1);
            await Settle(120);
            Frame(5);
        }
        pm.Move(-100);
        await Settle(150);

        foreach (var s in new[] { Scope.Links, Scope.Images, Scope.Screenshots, Scope.Code, Scope.Colors, Scope.All })
        {
            pm.Scope = s;
            await pm.Reload(false);
            if (pm.Items.Count > 0) pm.Selected = pm.Items[0];
            await Settle(650);
            Frame(s is Scope.Images or Scope.Screenshots ? 11 : 8);
        }

        string word = app.Settings.Language == "tr" ? "fatura" : "invoice";
        var box = root.FindChild<TextBox>();
        for (int i = 1; i <= word.Length; i++)
        {
            pm.Search = word[..i];
            if (box != null) { box.Text = word[..i]; box.CaretIndex = i; }
            await pm.Reload(false);
            if (pm.Items.Count > 0) pm.Selected = pm.Items[0];
            await Settle(i == word.Length ? 700 : 160);
            Frame(i == word.Length ? 26 : 3);
        }
        panel.Close();
    }
}
