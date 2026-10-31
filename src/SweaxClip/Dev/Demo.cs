using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.UI;

namespace SweaxClip.Dev;

/// <summary>
/// Developer modes that run on a throw-away archive:
///   --demo [--lang tr]           browse the library with sample data
///   --shots DIR [--lang tr]      render README screenshots (PNG, 2x)
///   --gif DIR [--lang tr]        render frames for the animated quick-panel demo
/// </summary>
public static class Demo
{
    public static async void Run(App app, string[] args)
    {
        string? Arg(string name) { int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        bool tr = Arg("--lang") == "tr";
        string dir = Path.Combine(Path.GetTempPath(), "sweaxclip-demo-" + (tr ? "tr" : "en"));
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Paths.Init(dir);
        var settings = Settings.Load(dir);
        settings.Language = tr ? "tr" : "en";
        Loc.Init(settings.Language);
        app.Settings = settings;
        app.Store = new ClipStore(Paths.Db);
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

    static void Render(FrameworkElement el, string file, double scale = 2)
    {
        el.UpdateLayout();
        double w = el.ActualWidth, h = el.ActualHeight;
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0B)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
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

    static async Task Shots(App app, string outDir)
    {
        // ── quick panel ──
        var panel = OpenPanel(app);
        var pm = panel.Model;
        var root = (FrameworkElement)panel.Content;

        async Task PanelShot(string name, Scope scope, string search = "", int select = 0)
        {
            pm.Search = search;
            pm.Scope = scope;
            await pm.Reload(false);
            if (pm.Items.Count > select) pm.Selected = pm.Items[select];
            var box = root.FindChild<TextBox>();
            if (box != null) box.Text = search;
            await Settle(700);
            Render(root, Path.Combine(outDir, name));
        }

        await PanelShot("panel-all.png", Scope.All);
        await PanelShot("panel-images.png", Scope.Images);
        await PanelShot("panel-links.png", Scope.Links);
        await PanelShot("panel-code.png", Scope.Code);
        await PanelShot("panel-colors.png", Scope.Colors);
        await PanelShot("panel-search.png", Scope.All, app.Settings.Language == "tr" ? "fatura" : "invoice");
        panel.Close();

        // ── library ──
        var lib = new LibraryWindow(app) { ShowActivated = false, Left = -6000, Top = 40, WindowStartupLocation = WindowStartupLocation.Manual, Width = 1440, Height = 900 };
        lib.Show();
        await Settle(900);
        var lm = lib.Model;
        var lroot = (FrameworkElement)lib.Content;

        async Task LibShot(string name, Scope scope, Func<ClipItem, bool>? pick = null, bool grid = true)
        {
            lib.CloseSettings();
            lm.GridPreferred = grid;
            lm.Search = "";
            lm.Scope = scope;
            await lm.Reload(false);
            if (pick != null && lm.Items.FirstOrDefault(pick) is { } hit) lm.Selected = hit;
            await Settle(1200);
            Render(lroot, Path.Combine(outDir, name));
        }

        await LibShot("library-all.png", Scope.All, c => c.Kind == ClipKind.Screenshot);
        await LibShot("library-screenshots.png", Scope.Screenshots, c => c.Height == 1000 && c.Width == 1400);
        await LibShot("library-code.png", Scope.Code, c => c.Text?.Contains("FindAsync") == true);
        await LibShot("library-colors.png", Scope.Colors, c => c.Text == "#FFE500");
        await LibShot("library-links.png", Scope.Links, c => c.Text?.Contains("figma") == true);
        await LibShot("library-images.png", Scope.Images, c => c.Kind == ClipKind.Image);

        // settings
        var settingsBtn = typeof(LibraryWindow).GetMethod("Settings_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        settingsBtn?.Invoke(lib, [lib, new RoutedEventArgs()]);
        await Settle(900);
        Render(lroot, Path.Combine(outDir, "library-settings.png"));
        lib.Close();
    }

    /// <summary>Frames of a scripted quick-panel session; stitched into a GIF with ffmpeg.</summary>
    static async Task Gif(App app, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var panel = OpenPanel(app);
        var pm = panel.Model;
        var root = (FrameworkElement)panel.Content;
        var shift = (TranslateTransform)root.RenderTransform;
        int n = 0;
        const double scale = 1.5;
        void Frame(int repeat = 1)
        {
            string first = Path.Combine(outDir, $"f{n:0000}.png");
            Render(root, first, scale);
            n++;
            for (int i = 1; i < repeat; i++) File.Copy(first, Path.Combine(outDir, $"f{n++:0000}.png"));
        }

        await pm.Reload(false);
        pm.Selected = pm.Items[0];
        await Settle(900);

        // appear
        for (int i = 0; i <= 5; i++)
        {
            double t = i / 5.0, e = 1 - Math.Pow(1 - t, 3);
            root.Opacity = t;
            shift.Y = 10 * (1 - e);
            await Settle(20);
            Frame();
        }
        Frame(12);

        // walk down the list
        for (int i = 0; i < 4; i++)
        {
            pm.Move(1);
            await Settle(120);
            Frame(5);
        }
        pm.Move(-100);
        await Settle(150);

        // tab through categories
        foreach (var s in new[] { Scope.Text, Scope.Links, Scope.Images, Scope.Screenshots, Scope.Code, Scope.Colors, Scope.All })
        {
            pm.Scope = s;
            await pm.Reload(false);
            await Settle(650);
            Frame(s == Scope.Images || s == Scope.Screenshots ? 12 : 9);
        }

        // search inside screenshots (OCR)
        string word = app.Settings.Language == "tr" ? "fatura" : "invoice";
        var box = root.FindChild<TextBox>();
        for (int i = 1; i <= word.Length; i++)
        {
            pm.Search = word[..i];
            if (box != null) { box.Text = word[..i]; box.CaretIndex = i; }
            await pm.Reload(false);
            await Settle(i == word.Length ? 700 : 160);
            Frame(i == word.Length ? 26 : 3);
        }
        panel.Close();
    }
}
