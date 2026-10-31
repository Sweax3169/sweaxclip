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
    IntPtr _hwnd, _previous;
    bool _pasting, _showing, _shellTarget;
    Point? _caretInPanel;
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(1300) };
    ClipItem? _lastSelected;
    bool _staggerNext;

    /// <summary>Tab button width without its label: 8 + 18 + 8.</summary>
    const double TabWidth = 34;

    /// <summary>Windows' own surfaces sit in a band above every app window; the panel can't be drawn over them.</summary>
    static readonly HashSet<string> ShellSurfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost", "SearchApp", "SearchUI", "StartMenuExperienceHost", "ShellExperienceHost", "Widgets", "ShellHost",
    };

    public ClipListModel Model => _model;

    public QuickPanel(App app)
    {
        _app = app;
        _model = new ClipListModel(app.Store, library: false) { DayHeaders = false };
        DataContext = _model;
        InitializeComponent();

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
            long ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)((ex | Win32.WS_EX_TOOLWINDOW) & ~Win32.WS_EX_APPWINDOW));
        };
        Deactivated += (_, _) => { if (!_pasting && IsVisible && !AnyMenuOpen()) HideNow(); };

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

    void UpdateCount()
    {
        var info = _model.Scopes.FirstOrDefault(s => s.Scope == _model.Scope);
        placeholder.Text = Loc.Format("SearchCount", info?.CountText ?? "0");
        count.Text = _model.Search.Length > 0 ? Loc.Format("Found", _model.Items.Count.ToString("N0", Loc.Culture)) : "";
    }

    static bool AnyMenuOpen() => Keyboard.FocusedElement is MenuItem || Mouse.Captured is ContextMenu;

    // ── show / hide ──────────────────────────────────────────────────────────

    public async void Toggle(IntPtr previous)
    {
        if (IsVisible) { HideNow(); return; }
        await ShowAt(previous);
    }

    public async Task ShowAt(IntPtr previous)
    {
        if (_showing) return;
        _showing = true;
        try
        {
            // Start, Search and the notification centre are drawn above every app window, so close them first;
            // a clip picked for them is pasted into Search when it reopens.
            _shellTarget = IsShellSurface(previous);
            if (_shellTarget)
            {
                Win32.Send(Win32.Key(Win32.VK_ESCAPE, false), Win32.Key(Win32.VK_ESCAPE, true));
                await Task.Delay(160);
                previous = Win32.GetForegroundWindow();
            }
            _previous = previous;
            if (_hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            pausedBadge.Visibility = _app.Settings.Paused ? Visibility.Visible : Visibility.Collapsed;
            shadowFx.Opacity = ThemeManager.IsDark ? 0.55 : 0.2;

            _model.Search = "";
            if (_model.Scope != Scope.All) _model.Scope = Scope.All;
            await _model.Reload(false);
            SetIndexes(false);
            UpdateCount();

            var (x, y) = Placement(_shellTarget ? IntPtr.Zero : previous);
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            host.Opacity = 0;
            Show();
            Win32.ForceForeground(_hwnd);
            Activate();
            search.Focus();
            Keyboard.Focus(search);
            sizer.Snap();
            UpdateLayout();
            list.ScrollIntoView(_model.Selected);
            Enter();
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
    (int x, int y) Placement(IntPtr previous)
    {
        var caret = previous == IntPtr.Zero ? null : CaretLocator.Find(previous);
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
        list.ScrollIntoView(sel);
        // the row opens over ~0.4 s; make sure all of it ends up in view
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(Springs.ManualClock ? 0 : 380);
            if (ReferenceEquals(_model.Selected, sel) && list.ItemContainerGenerator.ContainerFromItem(sel) is FrameworkElement row) row.BringIntoView();
        }, DispatcherPriority.Background);
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

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool searchEmpty = search.Text.Length == 0;

        switch (key)
        {
            case Key.LeftAlt or Key.RightAlt: SetIndexes(true); e.Handled = true; return;
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
            default:
                if (!search.IsKeyboardFocused) search.Focus();
                return;
        }
        e.Handled = true;
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

            await Leave();
            HideNow();
            if (_shellTarget) await Paster.PasteIntoSearch();
            else await Paster.PasteInto(_previous);
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
