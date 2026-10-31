using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// The "Deck" panel style. Win+V: a deck of clips that deals itself open where Windows would show its panel (under the text
/// cursor, or above the taskbar). There is no window frame — only the lid, the cards and their shadows.
/// </summary>
public partial class DeckPanel : Window, IQuickPanel
{
    readonly App _app;
    readonly ClipListModel _model;
    IntPtr _hwnd, _previous;
    bool _pasting, _showing, _shellTarget;
    Point? _caretInPanel;
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(1300) };

    public ClipListModel Model => _model;
    public Deck DeckView => deck;

    public DeckPanel(App app)
    {
        _app = app;
        _model = new ClipListModel(app.Store, library: false);
        DataContext = _model;
        InitializeComponent();
        deck.Model = _model;
        deck.Activated += item => Paste(item, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        // the lid and the hints stay attached to the stack, however few cards it has
        deck.EdgesChanged += (top, bottom) =>
        {
            lidFollow.Y = Math.Max(0, top);
            hintsFollow.Y = Math.Min(0, bottom + 6 - deck.ActualHeight);
        };

        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClipListModel.Scope) or nameof(ClipListModel.Total) or nameof(ClipListModel.Rows)) UpdateCount();
            if (e.PropertyName == nameof(ClipListModel.IsEmpty)) empty.Visibility = _model.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        };
        foreach (var s in _model.Scopes) s.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ScopeInfo.Count)) UpdateCount(); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Fade(toast, 0, 160); };

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
        if (_model.Search.Length > 0) count.Text = Loc.Format("ClipsStored", _model.Items.Count.ToString("N0", Loc.Culture));
        else count.Text = info == null ? "" : Loc.Format("ClipsStored", info.CountText);
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
            _shellTarget = QuickPanel.IsShellSurface(previous);
            if (_shellTarget)
            {
                Win32.Send(Win32.Key(Win32.VK_ESCAPE, false), Win32.Key(Win32.VK_ESCAPE, true));
                await Task.Delay(160);
                previous = Win32.GetForegroundWindow();
            }
            _previous = previous;
            if (_hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            pausedBadge.Visibility = _app.Settings.Paused ? Visibility.Visible : Visibility.Collapsed;

            _model.Search = "";
            if (_model.Scope != Scope.All) _model.Scope = Scope.All;
            await _model.Reload(false);
            SetIndexes(false);
            UpdateCount();

            var (x, y) = Placement(_shellTarget ? IntPtr.Zero : previous);
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            deck.Clear();
            Show();
            Win32.ForceForeground(_hwnd);
            Activate();
            search.Focus();
            Keyboard.Focus(search);
            UpdateLayout();
            Enter();
        }
        finally { _showing = false; }
    }

    /// <summary>The opening: the lid drops in, then the deck is dealt from front to back.</summary>
    public void Enter()
    {
        lidShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } });
        lid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        hints.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(220) });
        deck.Sync(Deck.Mode.Deal);
    }

    public void HideNow()
    {
        if (!IsVisible) return;
        SetIndexes(false);
        Hide();
        deck.Clear();
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

        int w = (int)(Width * scale), h = (int)(Height * scale), margin = (int)(20 * scale);
        var work = mi.rcWork;
        int x, y;
        bool hang = false;
        if (caret is { } r)
        {
            x = r.Left - margin - (int)(18 * scale);
            y = r.Bottom + (int)(4 * scale) - (int)(16 * scale);
            hang = true;
            if (y + h > work.Bottom) { y = r.Top - h + (int)(14 * scale); hang = false; }
        }
        else
        {
            x = work.Left + (work.Width - w) / 2;
            y = work.Bottom - h + (int)(6 * scale);
        }
        x = Math.Clamp(x, work.Left - margin, Math.Max(work.Left, work.Right - w + margin));
        y = Math.Clamp(y, work.Top - margin, Math.Max(work.Top, work.Bottom - h + margin));

        // below the caret the deck hangs from the lid; above the caret or the taskbar it rests on the bottom edge
        deck.AnchorBottom = !hang;
        empty.VerticalAlignment = hang ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        empty.Margin = hang ? new Thickness(0, 14, 0, 0) : new Thickness(0, 0, 0, 6);
        _caretInPanel = caret is { } cr ? new Point((cr.Left - x) / scale, (cr.Top - y) / scale) : null;
        return (x, y);
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
            case Key.Up: _model.Move(1); break;
            case Key.Down: _model.Move(-1); break;
            case Key.PageUp: _model.Move(5); break;
            case Key.PageDown: _model.Move(-5); break;
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

    void SetIndexes(bool show)
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

            // the card flies to where the text will land
            var deckOrigin = deck.TranslatePoint(new Point(0, 0), root);
            var target = _caretInPanel is { } p ? new Point(p.X - root.Margin.Left - deckOrigin.X, p.Y - root.Margin.Top - deckOrigin.Y) : new Point(deck.ActualWidth / 2, -120);
            deck.Fling(target);
            lid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)));
            hints.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
            await Task.Delay(150);
            HideNow();
            lid.BeginAnimation(OpacityProperty, null);
            hints.BeginAnimation(OpacityProperty, null);
            if (_shellTarget) await Paster.PasteIntoSearch();
            else await Paster.PasteInto(_previous);
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
            if (_previous != IntPtr.Zero && !_shellTarget) Win32.ForceForeground(_previous);
        }
    }

    void TogglePin(ClipItem? item)
    {
        if (item == null) return;
        item.Pinned = !item.Pinned;
        _app.Store.SetPinned(item.Id, item.Pinned);
        deck.Bump(item);
        Toast(item.Pinned ? Loc.T("Pinned") : Loc.T("Unpinned"));
        _ = _model.RefreshCounts();
    }

    void Delete(ClipItem? item)
    {
        if (item == null) return;
        _app.Store.Trash(item.Id);
        deck.Discard(item);
        _model.Drop(item);
        Toast(Loc.T("MovedToTrash"));
    }

    void OpenLibrary()
    {
        var sel = _model.Selected;
        HideNow();
        _app.ShowLibrary(sel?.Id);
    }

    void Toast(string text)
    {
        toastText.Text = text;
        Fade(toast, 1, 140);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    static void Fade(UIElement el, double to, int ms) =>
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)));
}
