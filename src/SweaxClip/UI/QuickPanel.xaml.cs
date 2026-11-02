using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// The "Timeline" panel style (default). Win+V: the clipboard timeline. It opens where Windows opens its own panel — under the text cursor,
/// or above the taskbar — and every movement in it runs on <see cref="Springs"/>.
/// </summary>
public partial class QuickPanel : Window, IQuickPanel
{
    readonly App _app;
    readonly ClipListModel _model;
    IntPtr _hwnd, _previous, _previousFocus;
    bool _pasting, _showing, _shellTarget, _warming;
    Point? _caretInPanel;
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(1300) };
    ClipItem? _lastSelected;
    Glide? _glide;
    ScrollViewer? _scroll;
    bool _staggerNext;

    /// <summary>Tab button width without its label: 8 + 18 + 8.</summary>
    const double TabWidth = 34;

    /// <summary>Windows' own surfaces sit in a band above every app window; the panel can't be drawn over them.</summary>
    static readonly HashSet<string> ShellSurfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost", "SearchApp", "SearchUI", "StartMenuExperienceHost", "ShellExperienceHost", "Widgets", "ShellHost",
    };

    public ClipListModel Model => _model;
    public IntPtr Target => _previous;
    /// <summary>Bench mode: open here (off-screen) and never take focus.</summary>
    internal static (int x, int y)? BenchPlace;
    internal static Action<string>? BenchStep;
    /// <summary>Bench mode on a real monitor: behind every window (and nearly transparent), so nobody sees it.</summary>
    internal static bool BenchBottom;

    public QuickPanel(App app)
    {
        _app = app;
        _model = new ClipListModel(app.Store, library: false) { DayHeaders = false };
        DataContext = _model;
        InitializeComponent();
        Caret.Attach(search);

        _model.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ClipListModel.Scope): UpdateCount(); MoveIndicator(); _staggerNext = IsVisible; break;
                case nameof(ClipListModel.Total): UpdateCount(); break;
                case nameof(ClipListModel.Rows):
                    UpdateCount();
                    if (_staggerNext) { _staggerNext = false; Dispatcher.BeginInvoke(Stagger, DispatcherPriority.Loaded); }
                    break;
                case nameof(ClipListModel.IsEmpty): empty.Visibility = _model.IsEmpty ? Visibility.Visible : Visibility.Collapsed; break;
                case nameof(ClipListModel.Selected): FollowSelection(); break;
            }
        };
        foreach (var s in _model.Scopes) s.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ScopeInfo.Count)) UpdateCount(); };
        search.TextChanged += (_, _) => placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
        search.GotKeyboardFocus += (_, _) => glass.Foreground = (Brush)FindResource("Accent");
        search.LostKeyboardFocus += (_, _) => glass.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Springs.To(toast, OpacityProperty, 0, 400, 1); };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // never activated: the app below keeps focus and its text cursor, so a paste lands where the user was typing
            long ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)((ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE) & ~Win32.WS_EX_APPWINDOW));
            if (!AllowsTransparency) WindowEffects.SeeThrough(_hwnd);
        };
        // the window is the panel's full height; outside the panel and its shadow it lets clicks through
        host.SizeChanged += (_, _) => FitRegion();
        PreviewMouseDown += (_, e) => { if (!host.IsMouseOver) { e.Handled = true; HideNow(); } };
        IsVisibleChanged += (_, _) => { if (!_warming) _app.CaptureKeys(IsVisible); };
        list.Loaded += (_, _) =>
        {
            if (_scroll != null || list.FindChild<ScrollViewer>() is not { } sv) return;
            _scroll = sv;
            _glide = new Glide(sv);
        };
        // the wheel glides too: a notch is about one row
        list.PreviewMouseWheel += (_, e) =>
        {
            if (_glide == null) return;
            e.Handled = true;
            _glide.By(-e.Delta * 0.9);
        };

        CommandBindings.Add(new CommandBinding(Cmds.Paste, (_, e) => Paste(e.Parameter as ClipItem, false)));
        CommandBindings.Add(new CommandBinding(Cmds.PastePlain, (_, e) => Paste(e.Parameter as ClipItem, true)));
        CommandBindings.Add(new CommandBinding(Cmds.Copy, (_, e) => CopyOnly(e.Parameter as ClipItem)));
        CommandBindings.Add(new CommandBinding(Cmds.TogglePin, (_, e) => TogglePin(e.Parameter as ClipItem)));
        CommandBindings.Add(new CommandBinding(Cmds.Delete, (_, e) => Delete(e.Parameter as ClipItem)));
        CommandBindings.Add(new CommandBinding(Cmds.Open, (_, e) => { if (e.Parameter is ClipItem c) { HideNow(); _app.OpenItem(c); } }));
        CommandBindings.Add(new CommandBinding(Cmds.ShowInFolder, (_, e) => { if (e.Parameter is ClipItem c) { HideNow(); _app.ShowInFolder(c); } }));
        CommandBindings.Add(new CommandBinding(Cmds.SaveImage, (_, e) => { if (e.Parameter is ClipItem c) { HideNow(); _app.SaveImageAs(c); } }));
        CommandBindings.Add(new CommandBinding(Cmds.SetScope, (_, e) => { if (e.Parameter is Scope s) { _model.Scope = s; search.Focus(); } }));
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new QuietPeer(this, "SweaxClip");

    void UpdateCount()
    {
        var info = _model.Scopes.FirstOrDefault(s => s.Scope == _model.Scope);
        placeholder.Text = Loc.Format("SearchCount", info?.CountText ?? "0");
        count.Text = _model.Search.Length > 0 ? Loc.Format("Found", _model.Items.Count.ToString("N0", Loc.Culture)) : "";
    }

    // ── show / hide ──────────────────────────────────────────────────────────

    public async void Toggle(IntPtr previous)
    {
        if (IsVisible) { HideNow(); return; }
        await ShowAt(previous);
    }

    public async Task ShowAt(IntPtr previous)
    {
        if (_showing) { Paths.Log("Win+V pressed again while the panel was still opening"); return; }
        _showing = true;
        long started = Environment.TickCount64;
        try
        {
            // Start, Search, Task View, the notification centre and the snipping overlay sit in bands above every app
            // window, where the panel can't be drawn, so close them first. A clip picked over Start or Search is pasted
            // into Search when it reopens; over the others it goes to the app that comes back to the front.
            _shellTarget = IsShellSurface(previous);
            if (_shellTarget || Layers.Band(previous) > 1 && !Layers.IsOurs(previous))
            {
                Win32.Send(Win32.Key(Win32.VK_ESCAPE, false), Win32.Key(Win32.VK_ESCAPE, true));
                await Task.Delay(160);
                previous = Win32.GetForegroundWindow();
            }
            _previous = previous;
            _previousFocus = _shellTarget ? IntPtr.Zero : Win32.FocusOf(previous);
            // the text cursor is looked up while the list loads; a slow app can't hold the panel up
            var caret = _shellTarget ? Task.FromResult<Win32.RECT?>(null) : CaretLocator.FindAsync(previous, 150);
            if (_hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            pausedBadge.Visibility = _app.Settings.Paused ? Visibility.Visible : Visibility.Collapsed;
            shadowFx.Opacity = ThemeManager.IsDark ? 0.55 : 0.2;

            BenchStep?.Invoke("start");
            _model.ResetQuietly();
            await _model.Reload(false);
            BenchStep?.Invoke("reload");
            SetIndexes(false);
            UpdateCount();

            var (x, y) = BenchPlace ?? Placement(await caret);
            Win32.SetWindowPos(_hwnd, BenchBottom ? new IntPtr(1) : Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            host.Opacity = 0;
            BenchStep?.Invoke("placed");
            Show();
            BenchStep?.Invoke("show");
            search.Focus();
            sizer.Snap();
            UpdateLayout();
            FitRegion();
            _glide?.Stop();
            list.ScrollIntoView(_model.Selected);
            BenchStep?.Invoke("layout");
            Enter();
            BenchStep?.Invoke("enter");
            long took = Environment.TickCount64 - started;
            if (took > 300) Paths.Log($"slow open: {took} ms over {Layers.Describe(previous)}");
            if (BenchPlace == null) _ = StayOnTop();
        }
        finally { _showing = false; }
    }

    internal static bool IsShellSurface(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return ShellSurfaces.Contains(p.ProcessName);
        }
        catch { return false; }
    }

    public async Task Warm()
    {
        if (IsVisible || _showing) return;
        if (_hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
        await _model.Reload(false);
        if (IsVisible || _showing) return;
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, -32000, -32000, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        host.Opacity = 0;
        _warming = true;
        try
        {
            Show();
            sizer.Snap();
            UpdateLayout();
            Hide();
        }
        finally { _warming = false; }
    }

    /// <summary>
    /// Clips the window to the panel and its shadow. A window that is not layered takes every click on its rectangle,
    /// transparent or not, so without this the empty space above a short panel would swallow clicks meant for the app.
    /// </summary>
    void FitRegion()
    {
        if (_hwnd == IntPtr.Zero || AllowsTransparency || !host.IsArrangeValid) return;
        var at = root.TranslatePoint(new Point(0, 0), this) + VisualTreeHelper.GetOffset(host);
        var dpi = VisualTreeHelper.GetDpi(this);
        double l = Math.Max(0, at.X - root.Margin.Left), t = Math.Max(0, at.Y - root.Margin.Top);
        double r = Math.Min(ActualWidth, at.X + host.ActualWidth + root.Margin.Right), b = Math.Min(ActualHeight, at.Y + host.ActualHeight + root.Margin.Bottom);
        var region = (l: (int)Math.Floor(l * dpi.DpiScaleX), t: (int)Math.Floor(t * dpi.DpiScaleY), r: (int)Math.Ceiling(r * dpi.DpiScaleX), b: (int)Math.Ceiling(b * dpi.DpiScaleY));
        if (region == _region) return;
        _region = region;
        Win32.SetWindowRgn(_hwnd, Win32.CreateRectRgn(region.l, region.t, region.r, region.b), true);
    }

    (int l, int t, int r, int b) _region;

    /// <summary>
    /// Another always-on-top window that came to the front later can hide the panel, and a second Win+V would then
    /// close it unseen. Raise the panel once if something covers it, and note what it was.
    /// </summary>
    async Task StayOnTop()
    {
        await Task.Delay(90);
        IntPtr over = IsVisible ? Layers.CoveredBy(_hwnd, host) : IntPtr.Zero;
        if (over == IntPtr.Zero) return;
        string what = Layers.Describe(over);
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        await Task.Delay(60);
        bool still = IsVisible && Layers.CoveredBy(_hwnd, host) != IntPtr.Zero;
        Paths.Log($"panel was covered by {what}{(still ? "; still covered after raising it" : "; raised above it")}");
    }

    /// <summary>The opening: the panel grows out of the caret (or the taskbar) and the rows settle in one after another.</summary>
    public void Enter()
    {
        UpdateLayout();
        host.RenderTransformOrigin = OriginTowardsCaret();
        Springs.To(host, OpacityProperty, 1, 900, 1, from: 0);
        Springs.To(hostScale, ScaleTransform.ScaleXProperty, 1, 320, 0.72, from: 0.92);
        Springs.To(hostScale, ScaleTransform.ScaleYProperty, 1, 320, 0.72, from: 0.92);
        Springs.Set(hostShift, TranslateTransform.YProperty, 0);
        MoveIndicator(instant: true);
        Stagger();
    }

    /// <summary>The first rows settle in one after another (on opening and on switching category).</summary>
    public void Stagger()
    {
        UpdateLayout();
        int i = 0;
        foreach (var item in _model.Items.Take(10))
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem row) continue;
            if (row.RenderTransform is not TranslateTransform shift) row.RenderTransform = shift = new TranslateTransform();
            double delay = 0.045 + i * 0.022;
            Springs.To(row, OpacityProperty, 1, 500, 1, from: 0, delay: delay);
            Springs.To(shift, TranslateTransform.YProperty, 0, 300, 0.8, from: 14, delay: delay);
            i++;
        }
    }

    /// <summary>Where the caret is, as a fraction of the panel — the point it grows from and shrinks back into.</summary>
    Point OriginTowardsCaret()
    {
        double w = host.ActualWidth, h = host.ActualHeight;
        if (w <= 0 || h <= 0) return new Point(0.5, host.VerticalAlignment == VerticalAlignment.Top ? 0 : 1);
        var at = host.TranslatePoint(new Point(0, 0), this);
        if (_caretInPanel is not { } c) return new Point(0.5, host.VerticalAlignment == VerticalAlignment.Top ? 0 : 1);
        return new Point(Math.Clamp((c.X - at.X) / w, 0, 1), Math.Clamp((c.Y - at.Y) / h, 0, 1));
    }

    public void HideNow()
    {
        if (!IsVisible) return;
        SetIndexes(false);
        Hide();
        _app.ScheduleTrim();
    }

    /// <summary>
    /// Same place Windows opens its clipboard panel: under the text cursor when one can be found,
    /// otherwise centred above the taskbar. Returns the window's top-left in physical pixels.
    /// </summary>
    (int x, int y) Placement(Win32.RECT? caret)
    {
        Win32.GetCursorPos(out var mouse);
        var anchor = caret is { } c ? new Win32.POINT { X = c.Left, Y = c.Bottom } : mouse;

        IntPtr mon = Win32.MonitorFromPoint(anchor, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(mon, ref mi);
        double scale = 1;
        if (Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) == 0) scale = dpi / 96.0;

        int w = (int)(Width * scale), h = (int)(Height * scale);
        int left = (int)(root.Margin.Left * scale), top = (int)(root.Margin.Top * scale), bottom = (int)(root.Margin.Bottom * scale);
        var work = mi.rcWork;
        int x, y;
        bool hang = false;
        if (caret is { } r)
        {
            x = r.Left - left - (int)(18 * scale);
            y = r.Bottom + (int)(6 * scale) - top;
            hang = true;
            if (y + h - bottom > work.Bottom) { y = r.Top - h + bottom - (int)(6 * scale); hang = false; }
        }
        else
        {
            x = work.Left + (work.Width - w) / 2;
            y = work.Bottom - h + bottom - (int)(12 * scale);
        }
        x = Math.Clamp(x, work.Left - left, Math.Max(work.Left, work.Right - w + left));
        y = Math.Clamp(y, work.Top - top, Math.Max(work.Top, work.Bottom - h + bottom));

        // below the caret the panel hangs from its top edge; above the caret or the taskbar it stands on its bottom edge
        host.VerticalAlignment = hang ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _caretInPanel = caret is { } cr ? new Point((cr.Left - x) / scale, ((hang ? cr.Bottom : cr.Top) - y) / scale) : null;
        return (x, y);
    }

    /// <summary>Lays the panel out for a recording, as if opened at <paramref name="caret"/> (window coordinates).</summary>
    public void PlaceForDemo(Point? caret, bool hang)
    {
        host.VerticalAlignment = hang ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _caretInPanel = caret;
        shadowFx.Opacity = ThemeManager.IsDark ? 0.55 : 0.2;
    }

    // ── selection and categories ─────────────────────────────────────────────

    void FollowSelection()
    {
        var sel = _model.Selected;
        if (sel == null || ReferenceEquals(sel, _lastSelected)) return;
        _lastSelected = sel;
        if (_glide == null || list.ItemContainerGenerator.ContainerFromItem(sel) is not FrameworkElement { IsVisible: true })
        {
            // far away (Home, End, a search hit): nothing in between to glide over, so jump there first
            _glide?.Stop();
            list.ScrollIntoView(sel);
        }
        // the row opens over ~0.4 s while its neighbour closes; keep all of it in view, gliding
        _glide?.Follow(KeepInView, 0.75);
    }

    /// <summary>Where the list should scroll so the selected row is fully visible, or null if it already is.</summary>
    double? KeepInView(double target)
    {
        if (_scroll == null || _model.Selected is not { } sel || list.ItemContainerGenerator.ContainerFromItem(sel) is not FrameworkElement { IsVisible: true } row) return null;
        const double margin = 10;
        double view = _scroll.ViewportHeight;
        double top = _scroll.VerticalOffset + row.TranslatePoint(new Point(0, 0), _scroll).Y, bottom = top + row.ActualHeight;
        if (bottom - top + 2 * margin >= view || top - margin < target) return top - margin;
        if (bottom + margin > target + view) return bottom + margin - view;
        return null;
    }

    void MoveIndicator(bool instant = false)
    {
        int i = _model.Scopes.ToList().FindIndex(s => s.Scope == _model.Scope);
        if (i < 0) return;
        indicator.Background = _model.Scopes[i].Hue ?? (Brush)FindResource("Accent");
        double x = i * TabWidth + 8;
        if (instant) Springs.Set(indicatorX, TranslateTransform.XProperty, x);
        else Springs.To(indicatorX, TranslateTransform.XProperty, x, 360, 0.78);
    }

    // ── keyboard ─────────────────────────────────────────────────────────────

    public void OnKey(CapturedKey k)
    {
        if (!IsVisible) return;
        var (key, mods) = PanelKeys.Read(k);
        if (key is Key.LeftAlt or Key.RightAlt) { SetIndexes(k.Down && !k.Ctrl); return; }
        if (!k.Down) return;
        if (HandleKey(key, mods)) return;
        PanelKeys.Edit(search, key, mods, PanelKeys.Text(k, _previous));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt) { SetIndexes(true); e.Handled = true; return; }
        if (HandleKey(key, Keyboard.Modifiers)) e.Handled = true;
        else if (!search.IsKeyboardFocused) search.Focus();
    }

    /// <summary>The panel's own keys. Returns false for keys that belong to the search box.</summary>
    bool HandleKey(Key key, ModifierKeys mods)
    {
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        bool searchEmpty = search.Text.Length == 0;

        switch (key)
        {
            case Key.Escape:
                if (!searchEmpty) _model.Search = "";
                else HideNow();
                break;
            case Key.Down: _model.Move(1); break;
            case Key.Up: _model.Move(-1); break;
            case Key.PageDown: _model.Move(5); break;
            case Key.PageUp: _model.Move(-5); break;
            case Key.Home when ctrl || searchEmpty: _model.Move(-100_000); break;
            case Key.End when ctrl || searchEmpty: _model.Move(100_000); break;
            case Key.Enter: Paste(_model.Selected, shift); break;
            case Key.Tab: _model.MoveScope(shift ? -1 : 1); break;
            case Key.Left when searchEmpty || ctrl: _model.MoveScope(-1); break;
            case Key.Right when searchEmpty || ctrl: _model.MoveScope(1); break;
            case >= Key.D1 and <= Key.D9 when mods == ModifierKeys.Alt:
                var nth = _model.Items.FirstOrDefault(i => i.Index == key - Key.D0);
                if (nth != null) Paste(nth, false);
                break;
            case >= Key.D1 and <= Key.D9 when ctrl:
                int n = key - Key.D0;
                if (n <= _model.Scopes.Count) _model.Scope = _model.Scopes[n - 1].Scope;
                break;
            case Key.Delete when searchEmpty || ctrl: Delete(_model.Selected); break;
            case Key.P when ctrl: TogglePin(_model.Selected); break;
            case Key.C when ctrl && search.SelectionLength == 0: CopyOnly(_model.Selected); break;
            case Key.O when ctrl: if (_model.Selected is { } o) { HideNow(); _app.OpenItem(o); } break;
            case Key.E when ctrl: OpenLibrary(); break;
            case Key.F when ctrl: search.Focus(); search.SelectAll(); break;
            default: return false;
        }
        return true;
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt) { SetIndexes(false); e.Handled = true; }
        base.OnPreviewKeyUp(e);
    }

    public void SetIndexes(bool show)
    {
        foreach (var c in _model.Items.Take(9)) c.ShowIndex = show;
    }

    // ── actions ──────────────────────────────────────────────────────────────

    async void Paste(ClipItem? item, bool plain)
    {
        if (item == null || _pasting) return;
        _pasting = true;
        try
        {
            if (!ReferenceEquals(item, _model.Selected)) _model.Selected = item;
            var full = await Task.Run(() => _app.Store.Get(item.Id)) ?? item;
            if (_app.Settings.PastePlainByDefault) plain = !plain;
            if (!Paster.SetClipboard(full, plain)) { Toast(Loc.T("ClipboardBusy")); return; }
            if (_app.Settings.MoveToTopOnPaste) _ = Task.Run(() => _app.Store.TouchId(item.Id));
            // from here on the keyboard is the app's again: whatever the user types next must not be lost to the panel
            _app.CaptureKeys(false);

            if (_shellTarget)
            {
                await Leave();
                HideNow();
                await Paster.PasteIntoSearch();
                return;
            }
            // the app never lost focus: paste at once while the panel folds back into the caret
            var leaving = Leave();
            await Paster.PasteInto(_previous, _previousFocus);
            await leaving;
            HideNow();
        }
        finally { _pasting = false; }
    }

    /// <summary>The panel folds back into the caret — where the clip is about to land.</summary>
    public async Task Leave()
    {
        host.RenderTransformOrigin = OriginTowardsCaret();
        Springs.To(hostScale, ScaleTransform.ScaleXProperty, 0.86, 700, 1);
        Springs.To(hostScale, ScaleTransform.ScaleYProperty, 0.86, 700, 1);
        Springs.To(host, OpacityProperty, 0, 900, 1);
        if (!Springs.ManualClock) await Task.Delay(130);
    }

    async void CopyOnly(ClipItem? item)
    {
        if (item == null) return;
        var full = await Task.Run(() => _app.Store.Get(item.Id)) ?? item;
        if (Paster.SetClipboard(full, false))
        {
            if (_app.Settings.MoveToTopOnPaste) _ = Task.Run(() => _app.Store.TouchId(item.Id));
            HideNow();
            if (_previous != IntPtr.Zero && !_shellTarget) Win32.ForceForeground(_previous);
        }
    }

    void TogglePin(ClipItem? item)
    {
        if (item == null) return;
        item.Pinned = !item.Pinned;
        _app.Store.SetPinned(item.Id, item.Pinned);
        Toast(item.Pinned ? Loc.T("Pinned") : Loc.T("Unpinned"));
        _ = _model.RefreshCounts();
    }

    /// <summary>The row folds away, then the clip goes to the trash.</summary>
    async void Delete(ClipItem? item)
    {
        if (item == null) return;
        _app.Store.Trash(item.Id);
        if (list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row)
        {
            if (row.LayoutTransform is not ScaleTransform fold) row.LayoutTransform = fold = new ScaleTransform();
            Springs.To(row, OpacityProperty, 0, 900, 1);
            Springs.To(fold, ScaleTransform.ScaleYProperty, 0, 520, 1);
            if (!Springs.ManualClock) await Task.Delay(170);
        }
        _model.Drop(item);
        Toast(Loc.T("MovedToTrash"));
    }

    void OpenLibrary()
    {
        var sel = _model.Selected;
        HideNow();
        _app.ShowLibrary(sel?.Id);
    }

    void Library_Click(object sender, RoutedEventArgs e) => OpenLibrary();

    void Toast(string text)
    {
        toastText.Text = text;
        Springs.To(toast, OpacityProperty, 1, 600, 1);
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
