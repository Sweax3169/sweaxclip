using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>The Win+V panel: opens at the caret, everything first, categories one Tab away.</summary>
public partial class QuickPanel : Window
{
    readonly App _app;
    readonly ClipListModel _model;
    IntPtr _hwnd, _previous;
    bool _pasting;
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(1300) };

    public ClipListModel Model => _model;

    public QuickPanel(App app)
    {
        _app = app;
        _model = new ClipListModel(app.Store, library: false);
        DataContext = _model;
        InitializeComponent();
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClipListModel.Selected)) ScrollToSelected();
        };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Fade(toast, 0); };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // keep out of Alt+Tab and the taskbar
            long ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)((ex | Win32.WS_EX_TOOLWINDOW) & ~Win32.WS_EX_APPWINDOW));
            WindowEffects.Apply(_hwnd, small: true);
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

    static bool AnyMenuOpen() => Keyboard.FocusedElement is MenuItem || Mouse.Captured is ContextMenu;

    // ── show / hide ──────────────────────────────────────────────────────────

    public async void Toggle(IntPtr previous)
    {
        if (IsVisible) { HideNow(); return; }
        await ShowAt(previous);
    }

    public async Task ShowAt(IntPtr previous)
    {
        _previous = previous;
        if (_hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
        pausedBadge.Visibility = _app.Settings.Paused ? Visibility.Visible : Visibility.Collapsed;

        _model.Search = "";
        if (_model.Scope != Scope.All) _model.Scope = Scope.All;
        var reload = _model.Reload(false);

        Position(previous);
        root.Opacity = 0;
        Show();
        Win32.ForceForeground(_hwnd);
        Activate();
        search.Focus();
        Keyboard.Focus(search);
        await reload;
        list.ScrollIntoView(_model.Rows.FirstOrDefault());
        ((Storyboard)Resources["Enter"]).Begin(this);
    }

    public void HideNow()
    {
        if (!IsVisible) return;
        Hide();
        _app.ScheduleTrim();
    }

    void Position(IntPtr previous)
    {
        Win32.POINT pt;
        bool caret = false;
        uint thread = previous != IntPtr.Zero ? Win32.GetWindowThreadProcessId(previous, out _) : 0;
        var gui = new Win32.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.GUITHREADINFO>() };
        if (thread != 0 && Win32.GetGUIThreadInfo(thread, ref gui) && gui.hwndCaret != IntPtr.Zero && (gui.rcCaret.Bottom > 0 || gui.rcCaret.Left > 0))
        {
            pt = new Win32.POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Bottom };
            Win32.ClientToScreen(gui.hwndCaret, ref pt);
            caret = true;
        }
        else Win32.GetCursorPos(out pt);

        IntPtr mon = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(mon, ref mi);
        double scale = 1;
        if (Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) == 0) scale = dpi / 96.0;

        int w = (int)(Width * scale), h = (int)(Height * scale), gap = (int)(10 * scale);
        var work = mi.rcWork;
        int x = pt.X - (caret ? 0 : w / 2);
        int y = pt.Y + gap;
        if (y + h > work.Bottom) y = (caret ? pt.Y - h - gap - (int)(18 * scale) : pt.Y - h - gap);
        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - h - gap));
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
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
            case Key.Escape:
                if (!searchEmpty) _model.Search = "";
                else HideNow();
                break;
            case Key.Down: _model.Move(1); break;
            case Key.Up: _model.Move(-1); break;
            case Key.PageDown: _model.Move(7); break;
            case Key.PageUp: _model.Move(-7); break;
            case Key.Home when ctrl || searchEmpty: _model.Move(-100_000); break;
            case Key.End when ctrl || searchEmpty: _model.Move(100_000); break;
            case Key.Enter: Paste(_model.Selected, shift); break;
            case Key.Tab: _model.MoveScope(shift ? -1 : 1); ScrollScope(); break;
            case Key.Left when searchEmpty || ctrl: _model.MoveScope(-1); ScrollScope(); break;
            case Key.Right when searchEmpty || ctrl: _model.MoveScope(1); ScrollScope(); break;
            case >= Key.D1 and <= Key.D9 when mods == ModifierKeys.Alt:
                var nth = _model.Items.FirstOrDefault(i => i.Index == key - Key.D0);
                if (nth != null) Paste(nth, false);
                break;
            case >= Key.D1 and <= Key.D9 when ctrl:
                int n = key - Key.D0;
                if (n <= _model.Scopes.Count) { _model.Scope = _model.Scopes[n - 1].Scope; ScrollScope(); }
                break;
            case Key.Delete when searchEmpty || ctrl: Delete(_model.Selected); break;
            case Key.P when ctrl: TogglePin(_model.Selected); break;
            case Key.C when ctrl && search.SelectionLength == 0: CopyOnly(_model.Selected); break;
            case Key.O when ctrl: if (_model.Selected is { } o) { HideNow(); _app.OpenItem(o); } break;
            case Key.E when ctrl: OpenLibrary_Click(this, new RoutedEventArgs()); break;
            case Key.F when ctrl: search.Focus(); search.SelectAll(); break;
            default:
                if (!search.IsKeyboardFocused) search.Focus();
                return;
        }
        e.Handled = true;
    }

    void ScrollScope()
    {
        Dispatcher.BeginInvoke(() =>
        {
            int i = _model.Scopes.ToList().FindIndex(s => s.Active);
            double target = i * 70 - 120;
            scopeScroll.ScrollToHorizontalOffset(Math.Max(0, target));
        }, DispatcherPriority.Loaded);
    }

    void ScrollToSelected()
    {
        if (_model.Selected is not { } s) return;
        if (_model.Items.IndexOf(s) == 0) { list.ScrollIntoView(_model.Rows.FirstOrDefault()); return; }
        var row = _model.RowOf(s);
        if (row != null) list.ScrollIntoView(row);
    }

    void List_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && d.FindParent<Button>() != null) return;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ClipItem item)
        {
            e.Handled = true;
            Paste(item, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
    }

    // ── actions ──────────────────────────────────────────────────────────────

    async void Paste(ClipItem? item, bool plain)
    {
        if (item == null || _pasting) return;
        _pasting = true;
        try
        {
            var full = await Task.Run(() => _app.Store.Get(item.Id)) ?? item;
            if (_app.Settings.PastePlainByDefault) plain = !plain;
            if (!Paster.SetClipboard(full, plain)) { Toast("Clipboard busy"); return; }
            if (_app.Settings.MoveToTopOnPaste) _ = Task.Run(() => _app.Store.TouchId(item.Id));
            HideNow();
            await Paster.PasteInto(_previous);
        }
        finally { _pasting = false; }
    }

    async void CopyOnly(ClipItem? item)
    {
        if (item == null) return;
        var full = await Task.Run(() => _app.Store.Get(item.Id)) ?? item;
        if (Paster.SetClipboard(full, false))
        {
            if (_app.Settings.MoveToTopOnPaste) _ = Task.Run(() => _app.Store.TouchId(item.Id));
            HideNow();
            if (_previous != IntPtr.Zero) Win32.ForceForeground(_previous);
        }
    }

    void TogglePin(ClipItem? item)
    {
        if (item == null) return;
        item.Pinned = !item.Pinned;
        _app.Store.SetPinned(item.Id, item.Pinned);
        Toast(item.Pinned ? Loc.T("Pin") : Loc.T("Unpin"));
        _ = _model.RefreshCounts();
    }

    void Delete(ClipItem? item)
    {
        if (item == null) return;
        _app.Store.Trash(item.Id);
        _model.Drop(item);
        Toast(Loc.T("Delete"));
    }

    void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        var sel = _model.Selected;
        HideNow();
        _app.ShowLibrary(sel?.Id);
    }

    void Toast(string text)
    {
        toastText.Text = text;
        Fade(toast, 1);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    static void Fade(UIElement el, double to) =>
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)));
}
