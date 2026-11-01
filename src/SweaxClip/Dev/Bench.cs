using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Text;
using System.Windows;
using System.Windows.Media;
using SweaxClip.Core;
using SweaxClip.Native;
using SweaxClip.UI;

namespace SweaxClip.Dev;

/// <summary>
/// Opens and closes the Win+V panel off-screen and times it: how long until it shows, and how evenly its frames
/// arrive while it opens and while the selection moves. Nothing appears on the visible screen and no window takes focus.
///   --bench FILE [--trim]   (--trim empties the working set between opens, as 1.5.3 did after every close)
/// </summary>
public static class Bench
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly List<double> Frames = [];

    static void OnFrame(object? sender, EventArgs e) => Frames.Add(Clock.Elapsed.TotalMilliseconds);

    public static async Task Run(App app, string file, bool trim, string[] args)
    {
        var steps = new List<string>();
        double last = 0;
        QuickPanel.BenchStep = name => { double now = Clock.Elapsed.TotalMilliseconds; steps.Add($"{name} +{now - last:0.0}"); last = now; };
        Output = file;
        var report = new StringBuilder();
        report.AppendLine($"SweaxClip bench · {DateTime.Now:yyyy-MM-dd HH:mm} · trim between opens: {trim} · render tier {RenderCapability.Tier >> 16}");
        QuickPanel.BenchPlace = (-6000, 60);
        var panel = new QuickPanel(app) { ShowActivated = false };
        CompositionTarget.Rendering += OnFrame;
        await Task.Delay(800);
        if (args.Contains("--warm"))
        {
            double w0 = Clock.Elapsed.TotalMilliseconds;
            await panel.Warm();
            report.AppendLine($"warm-up {Clock.Elapsed.TotalMilliseconds - w0:0} ms");
            await Task.Delay(500);
        }
        var box = (System.Windows.Controls.TextBox)panel.FindName("search");

        for (int run = 0; run < 7; run++)
        {
            if (trim)
            {
                ThumbLoader.Clear();
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                Win32.TrimWorkingSet();
            }
            await Task.Delay(1200);

            Frames.Clear();
            steps.Clear();
            double t0 = Clock.Elapsed.TotalMilliseconds;
            last = t0;
            IntPtr before = Win32.GetForegroundWindow();
            panel.Toggle(IntPtr.Zero);
            while (!panel.IsVisible && Clock.Elapsed.TotalMilliseconds - t0 < 3000) await Task.Delay(1);
            double shown = Clock.Elapsed.TotalMilliseconds - t0;
            await Task.Delay(30);
            bool stillFront = Win32.GetForegroundWindow() == before;
            report.AppendLine($"        focus stays with the app below: {stillFront} · search box has keyboard focus: {box.IsKeyboardFocused}");
            await Task.Delay(750);
            var open = Stats(Frames.Where(f => f >= t0).ToList());
            double firstFrame = Frames.FirstOrDefault(f => f >= t0 + shown) - t0;

            Frames.Clear();
            for (int i = 0; i < 8; i++)
            {
                panel.Model.Move(1);
                await Task.Delay(120);
            }
            await Task.Delay(300);
            var nav = Stats(Frames.ToList());

            if (run == 2) { report.AppendLine("        keys: " + await Keys(panel, box)); continue; }

            panel.HideNow();
            report.AppendLine($"open {run + 1}: shown after {shown,6:0.0} ms · first frame {firstFrame,6:0.0} ms · opening frames {open} · browsing frames {nav}");
            report.AppendLine("        " + string.Join("  ", steps));
        }
        CompositionTarget.Rendering -= OnFrame;
        using (var p = Process.GetCurrentProcess()) report.AppendLine($"working set {p.WorkingSet64 / 1048576} MB · private {p.PrivateMemorySize64 / 1048576} MB");
        File.WriteAllText(file, report.ToString());
    }

    static string Output = "";

    /// <summary>A picture of the panel as it is drawn (adorners included), to check the caret and the search text.</summary>
    static void Snap(Window w, string file)
    {
        var dpi = VisualTreeHelper.GetDpi(w);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w.ActualWidth * dpi.DpiScaleX), (int)(w.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(w);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    static CapturedKey K(int vk, bool shift = false, bool ctrl = false, bool alt = false) =>
        new(vk, (int)Win32.MapVirtualKey((uint)vk, 0), true, false, ctrl, shift, alt);

    /// <summary>Feeds the panel keys the way the hook does and checks what they did (never Enter: that would paste for real).</summary>
    static async Task<string> Keys(QuickPanel panel, System.Windows.Controls.TextBox box)
    {
        var log = new List<string>();
        void Check(string what, bool ok) => log.Add($"{what} {(ok ? "ok" : "FAIL")}");
        var first = panel.Model.Selected;
        panel.OnKey(K(0x28));                                   // Down
        Check("down-moves", !ReferenceEquals(first, panel.Model.Selected));
        panel.OnKey(K(0x26));                                   // Up
        Check("up-moves-back", ReferenceEquals(first, panel.Model.Selected));
        foreach (int vk in new[] { 0x4D, 0x45, 0x45, 0x54 }) panel.OnKey(K(vk)); // m e e t
        Check($"typing \"{box.Text}\"", box.Text == "meet");
        panel.OnKey(K(0x4D, shift: true));                      // M
        Check($"shift \"{box.Text}\"", box.Text == "meetM");
        panel.OnKey(K(0x08));                                   // Backspace
        Check("backspace", box.Text == "meet");
        await Task.Delay(250);
        Snap(panel, Path.ChangeExtension(Output, ".typing.png"));
        panel.OnKey(K(0x25));                                   // Left: caret, not category, while typing
        Check("left-moves-caret", box.CaretIndex == 3 && panel.Model.Scope == Scope.All);
        panel.OnKey(K(0x51, ctrl: true, alt: true));            // AltGr+Q
        Check($"altgr \"{box.Text}\"", box.Text.Length == 5);
        panel.OnKey(K(0x41, ctrl: true));                       // Ctrl+A
        Check("ctrl-a", box.SelectionLength == box.Text.Length);
        await Task.Delay(300);
        Check($"search filtered to {panel.Model.Items.Count}", panel.Model.Search == box.Text);
        panel.OnKey(K(0x1B));                                   // Esc clears
        Check("esc-clears", box.Text.Length == 0 && panel.IsVisible);
        panel.OnKey(K(0x09));                                   // Tab: next category
        Check("tab-category", panel.Model.Scope != Scope.All);
        panel.OnKey(K(0x09, shift: true));
        Check("shift-tab-back", panel.Model.Scope == Scope.All);
        panel.OnKey(K(0xA4));                                   // Alt shows 1-9
        Check("alt-indexes", panel.Model.Items.Take(1).All(i => i.ShowIndex));
        panel.OnKey(new CapturedKey(0xA4, 0x38, false, false, false, false, false));
        Check("alt-up-hides", panel.Model.Items.Take(1).All(i => !i.ShowIndex));
        panel.OnKey(K(0x1B));                                   // Esc closes
        Check("esc-closes", !panel.IsVisible);
        return string.Join(" · ", log);
    }

    /// <summary>Frame pacing: how many frames, the typical gap, the 95th percentile and the worst gap (ms).</summary>
    static string Stats(List<double> stamps)
    {
        if (stamps.Count < 3) return "(too few frames)";
        var gaps = stamps.Zip(stamps.Skip(1), (a, b) => b - a).OrderBy(g => g).ToList();
        double median = gaps[gaps.Count / 2], p95 = gaps[(int)(gaps.Count * 0.95)], worst = gaps[^1];
        int long_ = gaps.Count(g => g > 25);
        return $"n={stamps.Count,3} median {median,5:0.0} p95 {p95,5:0.0} worst {worst,6:0.0} >25ms {long_,2}";
    }
}
