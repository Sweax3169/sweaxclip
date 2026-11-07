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
        if (args.Contains("--onscreen"))
        {
            // on the primary monitor where the panel really opens, but under every window and at 2% opacity
            var wa = SystemParameters.WorkArea;
            QuickPanel.BenchPlace = ((int)(wa.Left + (wa.Width - panel.Width) / 2), (int)(wa.Bottom - panel.Height));
            QuickPanel.BenchBottom = true;
            panel.Topmost = false;
            if (panel.AllowsTransparency) panel.Opacity = 0.02;
            else if (panel.Content is UIElement content) content.Opacity = 0.02;
        }
        CompositionTarget.Rendering += OnFrame;
        await Task.Delay(800);
        if (args.Contains("--warm"))
        {
            double w0 = Clock.Elapsed.TotalMilliseconds;
            await panel.Warm();
            report.AppendLine($"warm-up {Clock.Elapsed.TotalMilliseconds - w0:0} ms");
            await Task.Delay(500);
        }
        if (args.Contains("--seethrough")) { File.WriteAllText(file, await SeeThrough(panel)); return; }
        if (args.Contains("--scrolltest")) { File.WriteAllText(file, await ScrollTest(app, panel)); return; }
        var box = (System.Windows.Controls.TextBox)panel.FindName("search");
        int ui = (int)Win32.GetCurrentThreadId();
        report.AppendLine($"clips: {app.Store.Counts()[Scope.All]} · window layered: {panel.AllowsTransparency}");
        report.AppendLine($"in front: {Layers.Describe(Win32.GetForegroundWindow())} · admin above us: {Layers.IsAdminAboveUs(Win32.GetForegroundWindow())}");

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
            var cpu0 = ThreadTimes();
            panel.Toggle(IntPtr.Zero);
            while (!panel.IsVisible && Clock.Elapsed.TotalMilliseconds - t0 < 3000) await Task.Delay(1);
            double shown = Clock.Elapsed.TotalMilliseconds - t0;
            await Task.Delay(30);
            bool stillFront = Win32.GetForegroundWindow() == before;
            await Task.Delay(750);
            var open = Stats(Frames.Where(f => f >= t0).ToList());
            string openCpu = Busiest(cpu0, ThreadTimes(), Clock.Elapsed.TotalMilliseconds - t0, ui);
            double firstFrame = Frames.FirstOrDefault(f => f >= t0 + shown) - t0;

            // where the selected row sits on screen, frame by frame: smooth motion moves it a little each frame, a jump moves it a lot
            var track = new List<(double t, double y, double off)>();
            var seen = new List<(object item, double y)>();
            var trace = new StringBuilder("t;id;y;height;offset;glideSet;glideRunning;extent;event\n");
            string pending = "";
            var prevStep2 = QuickPanel.BenchStep;
            int jumpsAsked = 0, corrections = 0;
            double lastOff = double.NaN;
            var prevStep = QuickPanel.BenchStep;
            QuickPanel.BenchStep = n => { if (n == "jump") jumpsAsked++; prevStep?.Invoke(n); };
            var list = (System.Windows.Controls.ListBox)panel.FindName("list");
            var sv = FindScroll(list);
            EventHandler onTrack = (_, _) =>
            {
                if (panel.Model.Selected is { } sel && list.ItemContainerGenerator.ContainerFromItem(sel) is FrameworkElement row && row.IsVisible)
                {
                    track.Add((Clock.Elapsed.TotalMilliseconds, row.TranslatePoint(new Point(0, 0), list).Y, sv?.VerticalOffset ?? 0));
                    seen.Add((sel, track[^1].y));
                    var gl2 = panel.GlideForBench;
                    trace.Append($"{Clock.Elapsed.TotalMilliseconds:0.0};{sel.Id};{track[^1].y:0.0};{row.ActualHeight:0.0};{sv?.VerticalOffset:0.0};{gl2?.LastSet:0.0};{gl2?.Running};{sv?.ExtentHeight:0};{pending}\n");
                    pending = "";
                }
                // the panel moved the list by itself: the offset is not what the glide set
                if (sv != null && panel.GlideForBench is { } gl && !double.IsNaN(gl.LastSet) && !double.IsNaN(lastOff) && Math.Abs(sv.VerticalOffset - lastOff) > 30 && Math.Abs(sv.VerticalOffset - gl.LastSet) > 30) corrections++;
                lastOff = sv?.VerticalOffset ?? double.NaN;
            };
            CompositionTarget.Rendering += onTrack;
            // a press every 120 ms, then a held key repeating every 33 ms
            Frames.Clear();
            double n0 = Clock.Elapsed.TotalMilliseconds;
            cpu0 = ThreadTimes();
            for (int i = 0; i < 8; i++) { pending = "move"; panel.Model.Move(1); await Task.Delay(120); }
            await Task.Delay(300);
            var nav = Stats(Frames.ToList());
            string navCpu = Busiest(cpu0, ThreadTimes(), Clock.Elapsed.TotalMilliseconds - n0, ui);
            Frames.Clear();
            n0 = Clock.Elapsed.TotalMilliseconds;
            cpu0 = ThreadTimes();
            for (int i = 0; i < 24; i++) { pending = "move"; panel.Model.Move(i < 12 ? 1 : -1); await Task.Delay(33); }
            await Task.Delay(400);
            var held = Stats(Frames.ToList());
            string heldCpu = Busiest(cpu0, ThreadTimes(), Clock.Elapsed.TotalMilliseconds - n0, ui);
            CompositionTarget.Rendering -= onTrack;
            if (run == 1) File.WriteAllText(Path.ChangeExtension(Output, ".trace.csv"), trace.ToString());
            QuickPanel.BenchStep = prevStep;
            var jumps = track.Zip(track.Skip(1), (p, q) => (dy: Math.Abs(q.off - p.off), dt: q.t - p.t)).Where(x => x.dy > 30).ToList();
            var steps2 = track.Zip(track.Skip(1), (p, q) => Math.Abs(q.off - p.off)).ToList();
            // what the eye sees: how far the selected row moves on screen in one frame (same row in both frames)
            var moves = seen.Zip(seen.Skip(1), (a, b) => ReferenceEquals(a.item, b.item) ? Math.Abs(b.y - a.y) : 0).ToList();
            string seenStat = $"row on screen: jumps >30px {moves.Count(m => m > 30)}, largest {(moves.Count > 0 ? moves.Max() : 0):0} px";
            string motion = seenStat + $" · offset jumps >30px in one frame: {jumps.Count} · largest step {(steps2.Count > 0 ? steps2.Max() : 0):0.0} px · scrolled {steps2.Sum():0} px in all · row hidden {track.Count(x => x.y < -2 || x.y > list.ActualHeight)} frames";

            if (run == 0 && args.Contains("--onscreen")) PrintPanel(panel, Path.ChangeExtension(Output, ".window.png"));
            if (run == 2) { report.AppendLine("        keys: " + await Keys(panel, box)); continue; }

            panel.HideNow();
            report.AppendLine($"open {run + 1}: shown after {shown,6:0.0} ms · first frame {firstFrame,6:0.0} ms · focus kept {stillFront}");
            report.AppendLine($"        opening  {open} · cpu {openCpu}");
            report.AppendLine($"        presses  {nav} · cpu {navCpu}");
            report.AppendLine($"        held key {held} · cpu {heldCpu}");
            report.AppendLine($"        {motion} · jumps asked {jumpsAsked} · list moved itself {corrections}");
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

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }

    /// <summary>What the compositor holds for the window (PrintWindow, full content), alpha included, to check see-through corners.</summary>
    static void PrintPanel(Window w, string file)
    {
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        Win32.GetWindowRect(hwnd, out var r);
        var bmi = new BITMAPINFOHEADER { biSize = 40, biWidth = r.Width, biHeight = -r.Height, biPlanes = 1, biBitCount = 32 };
        IntPtr screen = Win32.GetDC(IntPtr.Zero), mem = Win32.CreateCompatibleDC(screen);
        IntPtr dib = CreateDIBSection(mem, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr old = Win32.SelectObject(mem, dib);
        try
        {
            Win32.PrintWindow(hwnd, mem, 2);
            var px = new byte[r.Width * r.Height * 4];
            System.Runtime.InteropServices.Marshal.Copy(bits, px, 0, px.Length);
            var src = System.Windows.Media.Imaging.BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null, px, r.Width * 4);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
            using var fs = File.Create(file);
            enc.Save(fs);
        }
        finally
        {
            Win32.SelectObject(mem, old);
            Win32.DeleteObject(dib);
            Win32.DeleteDC(mem);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
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

    /// <summary>
    /// Shows the panel for half a second in a corner of a side monitor and compares the pixels around it with the same
    /// spot before: where the panel is transparent they must match. Only numbers are kept, never the picture.
    /// </summary>
    static async Task<string> SeeThrough(QuickPanel panel)
    {
        var mons = Win32.Monitors();
        var mon = mons.FirstOrDefault(m => m.Left >= 2560, mons[0]);
        int w = (int)panel.Width, h = (int)panel.Height;
        var rect = new Win32.RECT { Left = mon.Left + 120, Top = mon.Bottom - h - 90, Right = mon.Left + 120 + w, Bottom = mon.Bottom - 90 };
        var before = Pixels(Win32.CaptureScreen(rect));
        QuickPanel.BenchPlace = (rect.Left, rect.Top);
        QuickPanel.BenchBottom = false;
        panel.Toggle(IntPtr.Zero);
        await Task.Delay(550);
        var after = Pixels(Win32.CaptureScreen(rect));
        panel.HideNow();
        bool Same(int x, int y)
        {
            int i = (y * w + x) * 4;
            return Math.Abs(before[i] - after[i]) <= 14 && Math.Abs(before[i + 1] - after[i + 1]) <= 14 && Math.Abs(before[i + 2] - after[i + 2]) <= 14;
        }
        int topSame = 0, topAll = 0;
        for (int y = 0; y < 8; y++) for (int x = 0; x < w; x++) { topAll++; if (Same(x, y)) topSame++; }
        int cornerSame = 0;
        for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++) if (Same(x, y)) cornerSame++;
        int mid = ((h - 200) * w + w / 2) * 4;
        return $"monitor {mon.Left},{mon.Top} · window layered {panel.AllowsTransparency}\n" +
               $"top band unchanged {topSame * 100 / topAll}% · top-left corner unchanged {cornerSame}/36\n" +
               $"panel body pixel BGR {after[mid]},{after[mid + 1]},{after[mid + 2]} (was {before[mid]},{before[mid + 1]},{before[mid + 2]})\n" +
               $"top band mean brightness before {Mean(before, w, 8):0} after {Mean(after, w, 8):0}";
    }

    /// <summary>
    /// Scrolls the panel and the library down, deletes a clip that is in view, lets the archive refresh the list (as it
    /// does after every change) and checks the view stayed where it was. Runs on a copy of the archive (--data).
    /// </summary>
    static async Task<string> ScrollTest(App app, QuickPanel panel)
    {
        var log = new StringBuilder();

        // the panel
        panel.Toggle(IntPtr.Zero);
        await Task.Delay(900);
        var plist = (System.Windows.Controls.ListBox)panel.FindName("list");
        var psv = FindScroll(plist)!;
        psv.ScrollToVerticalOffset(900);
        await Task.Delay(400);
        log.AppendLine("panel: " + await DeleteInView(app, panel, plist, psv, () => panel.Model.Reload(true)));
        panel.HideNow();

        // the library, off-screen and never activated
        var lib = new LibraryWindow(app) { ShowActivated = false, Left = -6000, Top = 60, Width = 1100, Height = 800, WindowStartupLocation = WindowStartupLocation.Manual };
        lib.SourceInitialized += (_, _) =>
        {
            IntPtr h = new System.Windows.Interop.WindowInteropHelper(lib).Handle;
            long ex = (long)Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongPtr(h, Win32.GWL_EXSTYLE, (IntPtr)(ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW));
        };
        lib.Show();
        await Task.Delay(1500);
        var llist = (System.Windows.Controls.ListBox)lib.FindName("list");
        var lsv = FindScroll(llist)!;
        foreach (bool grid in new[] { true, false })
        {
            lib.Model.GridPreferred = grid;
            await Task.Delay(600);
            lsv.ScrollToVerticalOffset(2200);
            await Task.Delay(500);
            log.AppendLine($"library ({(grid ? "grid" : "list")}): " + await DeleteInView(app, lib, llist, lsv, lib.Refresh));
            // the next page arriving while scrolled down
            double before = lsv.VerticalOffset;
            lsv.ScrollToVerticalOffset(lsv.ExtentHeight);
            await Task.Delay(800);
            double end = lsv.VerticalOffset;
            await lib.Model.LoadMore();
            await Task.Delay(400);
            log.AppendLine($"    next page: offset {end:0} -> {lsv.VerticalOffset:0} (clips loaded {lib.Model.Items.Count})");
        }
        lib.Close();
        return log.ToString();
    }

    /// <summary>Deletes a clip in the middle of the view the way its delete button does, refreshes, and reports how far the view moved.</summary>
    static async Task<string> DeleteInView(App app, Window w, System.Windows.Controls.ListBox list, System.Windows.Controls.ScrollViewer sv, Func<Task> refresh)
    {
        double before = sv.VerticalOffset;
        ClipItem? victim = null;
        foreach (var row in list.Items)
        {
            var c = row is GridRow g ? g.Items.FirstOrDefault() : row as ClipItem;
            if (c == null || list.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement el || !el.IsVisible) continue;
            double y = el.TranslatePoint(new Point(0, 0), sv).Y;
            if (y > sv.ViewportHeight * 0.3 && y < sv.ViewportHeight * 0.7) { victim = c; break; }
        }
        if (victim == null) return $"no clip in view (offset {before:0})";
        Cmds.Delete.Execute(victim, w);
        await Task.Delay(300);
        double afterDrop = sv.VerticalOffset;
        await refresh();
        await Task.Delay(400);
        double afterRefresh = sv.VerticalOffset;
        bool kept = Math.Abs(afterRefresh - before) < 150;
        return $"offset {before:0} -> {afterDrop:0} after delete -> {afterRefresh:0} after refresh · {(kept ? "kept" : "MOVED")}";
    }

    static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource b)
    {
        var c = new System.Windows.Media.Imaging.FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
        var px = new byte[c.PixelWidth * c.PixelHeight * 4];
        c.CopyPixels(px, c.PixelWidth * 4, 0);
        return px;
    }

    static double Mean(byte[] px, int w, int rows)
    {
        double sum = 0;
        for (int i = 0; i < w * rows * 4; i += 4) sum += (px[i] + px[i + 1] + px[i + 2]) / 3.0;
        return sum / (w * rows);
    }

    static System.Windows.Controls.ScrollViewer? FindScroll(DependencyObject d)
    {
        if (d is System.Windows.Controls.ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) if (FindScroll(VisualTreeHelper.GetChild(d, i)) is { } hit) return hit;
        return null;
    }

    static Dictionary<int, TimeSpan> ThreadTimes()
    {
        var d = new Dictionary<int, TimeSpan>();
        using var p = Process.GetCurrentProcess();
        foreach (ProcessThread t in p.Threads) { try { d[t.Id] = t.TotalProcessorTime; } catch { } }
        return d;
    }

    /// <summary>The three threads that used the most CPU over the interval, as a share of one core ("ui" is the UI thread).</summary>
    static string Busiest(Dictionary<int, TimeSpan> a, Dictionary<int, TimeSpan> b, double wallMs, int ui) =>
        string.Join(" ", b.Select(kv => (id: kv.Key, ms: (kv.Value - (a.TryGetValue(kv.Key, out var v) ? v : TimeSpan.Zero)).TotalMilliseconds))
            .OrderByDescending(x => x.ms).Take(3).Select(x => $"{(x.id == ui ? "ui" : "t" + x.id)}:{x.ms / wallMs * 100:0}%"));

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
