using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SweaxClip.Core;

namespace SweaxClip.UI;

/// <summary>The full archive: sidebar of categories, timeline or contact sheet, and an inspector.</summary>
public partial class LibraryWindow : Window
{
    readonly App _app;
    readonly ClipListModel _model;
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    readonly DispatcherTimer _detailDelay = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(60) };
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
    TaskCompletionSource<bool>? _confirm;
    SettingsView? _settings;

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(nameof(Detail), typeof(ClipItem), typeof(LibraryWindow));
    public ClipItem? Detail { get => (ClipItem?)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }

    public ClipListModel Model => _model;

    public LibraryWindow(App app)
    {
        _app = app;
        _model = new ClipListModel(app.Store, library: true) { AllowGrid = true, GridPreferred = app.Settings.LibraryGrid };
        DataContext = _model;
        InitializeComponent();
        Width = Math.Max(MinWidth, app.Settings.LibraryWidth);
        Height = Math.Max(MinHeight, app.Settings.LibraryHeight);

        _model.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ClipListModel.Selected):
                    _detailDelay.Stop();
                    _detailDelay.Start();
                    ScrollToSelected();
                    break;
                case nameof(ClipListModel.Scope):
                case nameof(ClipListModel.UseGrid):
                    UpdateHeader();
                    break;
                case nameof(ClipListModel.Total):
                    UpdateStats();
                    UpdateHeader();
                    break;
            }
        };
        foreach (var s in _model.Scopes) s.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ScopeInfo.Count)) UpdateHeader(); };

        _detailDelay.Tick += async (_, _) => { _detailDelay.Stop(); await LoadDetail(); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))); };
        _clock.Tick += (_, _) => { _model.RefreshTimes(); UpdateStats(); };

        SourceInitialized += (_, _) => WindowEffects.Apply(new WindowInteropHelper(this).Handle, small: false);
        StateChanged += (_, _) =>
        {
            frame.Padding = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            maxBtn.Content = WindowState == WindowState.Maximized ? Glyphs.Unmax : Glyphs.Maximize;
        };
        Loaded += async (_, _) =>
        {
            ((Storyboard)Resources["Blink"]).Begin(this, true);
            _clock.Start();
            UpdatePause();
            UpdateHeader();
            await _model.Reload(false);
            UpdateStats();
            search.Focus();
        };
        Closed += (_, _) =>
        {
            _clock.Stop();
            ((Storyboard)Resources["Blink"]).Stop(this);
            if (WindowState == WindowState.Normal)
            {
                _app.Settings.LibraryWidth = ActualWidth;
                _app.Settings.LibraryHeight = ActualHeight;
            }
            _app.Settings.LibraryGrid = _model.GridPreferred;
            _app.Settings.Save();
        };

        closeBtn.MouseEnter += (_, _) => closeBtn.Background = (System.Windows.Media.Brush)FindResource("Danger");
        closeBtn.MouseLeave += (_, _) => closeBtn.ClearValue(BackgroundProperty);

        CommandBindings.Add(new CommandBinding(Cmds.Select, (_, e) => { if (e.Parameter is ClipItem c) _model.Selected = c; }));
        CommandBindings.Add(new CommandBinding(Cmds.Copy, (_, e) => Copy(e.Parameter as ClipItem, false)));
        CommandBindings.Add(new CommandBinding(Cmds.Paste, (_, e) => Copy(e.Parameter as ClipItem, false)));
        CommandBindings.Add(new CommandBinding(Cmds.PastePlain, (_, e) => Copy(e.Parameter as ClipItem, true)));
        CommandBindings.Add(new CommandBinding(Cmds.TogglePin, (_, e) => TogglePin(e.Parameter as ClipItem)));
        CommandBindings.Add(new CommandBinding(Cmds.Delete, (_, e) => Delete(e.Parameter as ClipItem)));
        CommandBindings.Add(new CommandBinding(Cmds.Restore, (_, e) => Restore(e.Parameter as ClipItem)));
        CommandBindings.Add(new CommandBinding(Cmds.DeleteForever, (_, e) => DeleteForever(e.Parameter as ClipItem)));
        CommandBindings.Add(new CommandBinding(Cmds.Open, (_, e) => { if (e.Parameter is ClipItem c) _app.OpenItem(c); }));
        CommandBindings.Add(new CommandBinding(Cmds.ShowInFolder, (_, e) => { if (e.Parameter is ClipItem c) _app.ShowInFolder(c); }));
        CommandBindings.Add(new CommandBinding(Cmds.SaveImage, (_, e) => { if (e.Parameter is ClipItem c) _app.SaveImageAs(c); }));
        CommandBindings.Add(new CommandBinding(Cmds.CopyOcr, (_, e) =>
        {
            if (e.Parameter is ClipItem { Ocr: { Length: > 0 } t }) { try { Clipboard.SetText(t); Toast(Loc.T("Copied")); } catch { } }
        }));
        CommandBindings.Add(new CommandBinding(Cmds.SetScope, (_, e) =>
        {
            if (e.Parameter is Scope s) { CloseSettings(); _model.Scope = s; }
        }));
    }

    public async Task FocusClip(long? id)
    {
        if (id is not long v) return;
        if (_model.Scope != Scope.All) _model.Scope = Scope.All;
        _model.Search = "";
        await _model.Reload(false);
        var hit = _model.Items.FirstOrDefault(i => i.Id == v);
        if (hit != null) _model.Selected = hit;
    }

    public Task Refresh() => _model.Reload(true);

    // ── header / stats ───────────────────────────────────────────────────────

    void UpdateHeader()
    {
        var info = _model.Scopes.FirstOrDefault(s => s.Scope == _model.Scope);
        scopeCount.Text = info?.CountText ?? "";
        bool visual = _model.Scope is Scope.Images or Scope.Screenshots;
        viewToggle.Visibility = visual ? Visibility.Visible : Visibility.Collapsed;
        emptyTrash.Visibility = _model.Scope == Scope.Trash && (info?.Count ?? 0) > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (visual)
        {
            gridView.IsChecked = _model.GridPreferred;
            listView.IsChecked = !_model.GridPreferred;
        }
        UpdateColumns();
    }

    async void UpdateStats()
    {
        var (first, _) = await Task.Run(_app.Store.Stats);
        long disk = await Task.Run(Backup.FolderSize);
        statCount.Text = _model.Total.ToString("N0", Loc.Culture);
        statLabel.Text = Loc.Format("ClipsStored", "").Trim().ToUpper(Loc.Culture);
        statSince.Text = first > 0 ? Loc.Format("Since", TimeText.Local(first).ToString("d MMM yyyy", Loc.Culture)) : "";
        statDisk.Text = Loc.Format("OnDisk", Bytes.Format(disk));
    }

    void UpdatePause()
    {
        bool paused = _app.Settings.Paused;
        pauseBtn.Content = paused ? Glyphs.Play : Glyphs.Pause;
        pauseBtn.ToolTip = paused ? Loc.T("ResumeCapture") : Loc.T("PauseCapture");
        pauseBtn.Foreground = paused ? (System.Windows.Media.Brush)FindResource("Accent") : (System.Windows.Media.Brush)FindResource("Text2");
    }

    // ── detail ───────────────────────────────────────────────────────────────

    async Task LoadDetail()
    {
        var sel = _model.Selected;
        if (sel == null)
        {
            Detail = null;
            noSel.Visibility = Visibility.Visible;
            detail.Visibility = Visibility.Collapsed;
            return;
        }
        var full = sel.IsPartial || sel.IsTextual ? await Task.Run(() => _app.Store.Get(sel.Id)) ?? sel : sel;
        if (!ReferenceEquals(sel, _model.Selected)) return;
        if (!ReferenceEquals(full, sel)) { full.Pinned = sel.Pinned; full.ThumbImage = sel.ThumbImage; }
        sel.PropertyChanged -= Sel_Changed;
        sel.PropertyChanged += Sel_Changed;
        Detail = full;
        noSel.Visibility = Visibility.Collapsed;
        detail.Visibility = Visibility.Visible;

        foreach (UIElement el in preview.Children) el.Visibility = Visibility.Collapsed;
        switch (full.Kind)
        {
            case ClipKind.Image or ClipKind.Screenshot:
                pvImage.Visibility = Visibility.Visible;
                break;
            case ClipKind.Color:
                pvColor.Visibility = Visibility.Visible;
                break;
            case ClipKind.Link:
                pvLink.Visibility = Visibility.Visible;
                break;
            case ClipKind.Files:
                pvFiles.Visibility = Visibility.Visible;
                fileList.ItemsSource = full.Files.Take(500).Select(f =>
                {
                    bool exists = File.Exists(f) || Directory.Exists(f);
                    string extra = !exists ? Loc.T("Missing").ToUpper(Loc.Culture)
                        : File.Exists(f) ? Bytes.Format(new FileInfo(f).Length) : "";
                    return new { Icon = IconCache.ForFile(f), Name = Path.GetFileName(f.TrimEnd('\\')) is { Length: > 0 } n ? n : f, Detail = $"{extra}  ·  {f}" };
                }).ToList();
                break;
            default:
                pvText.Visibility = Visibility.Visible;
                string t = full.Text ?? "";
                pvText.Text = t.Length > 300_000 ? t[..300_000] + "\n\n…" : t;
                pvText.FontFamily = (System.Windows.Media.FontFamily)FindResource(full.Kind == ClipKind.Code ? "MonoFont" : "UiFont");
                pvText.FontSize = full.Kind == ClipKind.Code ? 12 : 13.5;
                pvText.TextWrapping = full.Kind == ClipKind.Code ? TextWrapping.NoWrap : TextWrapping.Wrap;
                pvText.HorizontalScrollBarVisibility = full.Kind == ClipKind.Code ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
                pvText.ScrollToHome();
                break;
        }

        bool trash = full.Deleted > 0;
        bool openable = full.Kind is ClipKind.Link or ClipKind.Files or ClipKind.Image or ClipKind.Screenshot;
        openAction.Visibility = !trash && openable ? Visibility.Visible : Visibility.Collapsed;
        folderAction.Visibility = !trash && (full.Kind is ClipKind.Files or ClipKind.Image or ClipKind.Screenshot) ? Visibility.Visible : Visibility.Collapsed;
        saveAction.Visibility = !trash && full.IsImage ? Visibility.Visible : Visibility.Collapsed;
        deleteAction.Visibility = trash ? Visibility.Collapsed : Visibility.Visible;
        pinAction.Visibility = trash ? Visibility.Collapsed : Visibility.Visible;
        restoreAction.Visibility = foreverAction.Visibility = trash ? Visibility.Visible : Visibility.Collapsed;
        ocrBlock.Visibility = full.HasOcr ? Visibility.Visible : Visibility.Collapsed;
        UpdatePinGlyph(full);
    }

    void Sel_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipItem.Pinned) && sender is ClipItem c && Detail is { } d && d.Id == c.Id)
        {
            d.Pinned = c.Pinned;
            UpdatePinGlyph(d);
        }
    }

    void UpdatePinGlyph(ClipItem c)
    {
        pinGlyph.Text = c.Pinned ? Glyphs.Pinned : Glyphs.Pin;
        pinGlyph.Foreground = c.Pinned ? (System.Windows.Media.Brush)FindResource("Accent") : (System.Windows.Media.Brush)FindResource("Text");
        pinAction.ToolTip = c.Pinned ? Loc.T("Unpin") : Loc.T("Pin");
    }

    void PreviewImage_Click(object sender, MouseButtonEventArgs e)
    {
        if (Detail is { } d) _app.OpenItem(d);
    }

    // ── actions ──────────────────────────────────────────────────────────────

    async void Copy(ClipItem? item, bool plain)
    {
        if (item == null) return;
        var full = await Task.Run(() => _app.Store.Get(item.Id)) ?? item;
        if (Paster.SetClipboard(full, plain))
        {
            Toast(Loc.T("Copied"));
            if (_app.Settings.MoveToTopOnPaste && full.Deleted == 0) _ = Task.Run(() => _app.Store.TouchId(item.Id));
        }
    }

    void TogglePin(ClipItem? item)
    {
        if (item == null) return;
        var target = _model.Items.FirstOrDefault(i => i.Id == item.Id) ?? item;
        target.Pinned = !target.Pinned;
        _app.Store.SetPinned(target.Id, target.Pinned);
        if (Detail is { } d && d.Id == target.Id) { d.Pinned = target.Pinned; UpdatePinGlyph(d); }
        Toast(target.Pinned ? Loc.T("Pin") : Loc.T("Unpin"));
        if (_model.Scope == Scope.Pinned && !target.Pinned) _model.Drop(target);
        else _ = _model.RefreshCounts();
    }

    void Delete(ClipItem? item)
    {
        if (item == null) return;
        var target = _model.Items.FirstOrDefault(i => i.Id == item.Id) ?? item;
        _app.Store.Trash(target.Id);
        _model.Drop(target);
        Toast(Loc.T("Delete"));
    }

    void Restore(ClipItem? item)
    {
        if (item == null) return;
        var target = _model.Items.FirstOrDefault(i => i.Id == item.Id) ?? item;
        _app.Store.Restore(target.Id);
        _model.Drop(target);
        Toast(Loc.T("Restore"));
    }

    async void DeleteForever(ClipItem? item)
    {
        if (item == null) return;
        if (!await Confirm(Loc.T("DeleteForeverConfirm"))) return;
        var target = _model.Items.FirstOrDefault(i => i.Id == item.Id) ?? item;
        var files = _app.Store.DeleteForever([target.Id]);
        App.DeleteFiles(files);
        _model.Drop(target);
    }

    async void EmptyTrash_Click(object sender, RoutedEventArgs e)
    {
        var ids = await Task.Run(_app.Store.TrashIds);
        if (ids.Count == 0) return;
        if (!await Confirm(Loc.Format("EmptyTrashConfirm", ids.Count))) return;
        var files = await Task.Run(() => _app.Store.DeleteForever(ids));
        App.DeleteFiles(files);
        await _model.Reload(false);
        UpdateStats();
    }

    Task<bool> Confirm(string text)
    {
        confirmText.Text = text;
        confirm.Visibility = Visibility.Visible;
        _confirm = new TaskCompletionSource<bool>();
        return _confirm.Task;
    }

    void ConfirmYes_Click(object sender, RoutedEventArgs e) { confirm.Visibility = Visibility.Collapsed; _confirm?.TrySetResult(true); }
    void ConfirmNo_Click(object sender, RoutedEventArgs e) { confirm.Visibility = Visibility.Collapsed; _confirm?.TrySetResult(false); }

    public void Toast(string text)
    {
        toastText.Text = text;
        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ── view ─────────────────────────────────────────────────────────────────

    void View_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _model.GridPreferred = gridView.IsChecked == true;
    }

    void List_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateColumns();

    void UpdateColumns()
    {
        double w = list.ActualWidth - 30;
        if (w > 0) _model.Columns = Math.Max(2, (int)(w / 232));
    }

    void List_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight > e.ExtentHeight - 600) _ = _model.LoadMore();
    }

    void ScrollToSelected()
    {
        if (_model.Selected is not { } s) return;
        var row = _model.RowOf(s);
        if (row != null) list.ScrollIntoView(row);
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (settingsHost.Visibility == Visibility.Visible) { CloseSettings(); return; }
        _settings ??= new SettingsView(_app, this);
        _settings.Refresh();
        settingsHost.Content = _settings;
        settingsHost.Visibility = Visibility.Visible;
        listArea.Visibility = inspector.Visibility = Visibility.Collapsed;
        foreach (var s in _model.Scopes) s.Active = false;
    }

    public void CloseSettings()
    {
        if (settingsHost.Visibility != Visibility.Visible) return;
        settingsHost.Visibility = Visibility.Collapsed;
        listArea.Visibility = inspector.Visibility = Visibility.Visible;
        foreach (var s in _model.Scopes) s.Active = s.Scope == _model.Scope;
    }

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        _app.SetPaused(!_app.Settings.Paused);
        UpdatePause();
        Toast(_app.Settings.Paused ? Loc.T("Paused") : Loc.T("ResumeCapture"));
    }

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (confirm.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) ConfirmNo_Click(this, e);
            else if (e.Key == Key.Enter) ConfirmYes_Click(this, e);
            e.Handled = true;
            return;
        }
        if (settingsHost.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { CloseSettings(); e.Handled = true; }
            return;
        }

        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        bool inSearch = search.IsKeyboardFocused;
        bool typing = Keyboard.FocusedElement is TextBox tb && !ReferenceEquals(tb, search);
        if (typing && e.Key != Key.Escape) return;
        bool grid = _model.UseGrid;

        switch (e.Key)
        {
            case Key.Escape:
                if (search.Text.Length > 0) _model.Search = "";
                else Close();
                break;
            case Key.Down: _model.Move(grid ? _model.Columns : 1); break;
            case Key.Up: _model.Move(grid ? -_model.Columns : -1); break;
            case Key.Right when grid && (!inSearch || search.Text.Length == 0): _model.Move(1); break;
            case Key.Left when grid && (!inSearch || search.Text.Length == 0): _model.Move(-1); break;
            case Key.PageDown: _model.Move(grid ? _model.Columns * 3 : 10); break;
            case Key.PageUp: _model.Move(grid ? -_model.Columns * 3 : -10); break;
            case Key.Enter: Copy(_model.Selected, shift); break;
            case Key.Tab when ctrl: _model.MoveScope(shift ? -1 : 1); break;
            case >= Key.D1 and <= Key.D9 when ctrl:
                int n = e.Key - Key.D0;
                if (n <= _model.Scopes.Count) _model.Scope = _model.Scopes[n - 1].Scope;
                break;
            case Key.D0 when ctrl: _model.Scope = Scope.Trash; break;
            case Key.Delete when !inSearch || search.Text.Length == 0 || ctrl:
                if (_model.Scope == Scope.Trash) DeleteForever(_model.Selected); else Delete(_model.Selected);
                break;
            case Key.P when ctrl: TogglePin(_model.Selected); break;
            case Key.C when ctrl && search.SelectionLength == 0 && pvText.SelectionLength == 0: Copy(_model.Selected, false); break;
            case Key.O when ctrl: if (_model.Selected is { } o) _app.OpenItem(o); break;
            case Key.F when ctrl: search.Focus(); search.SelectAll(); break;
            case Key.OemComma when ctrl: Settings_Click(this, e); break;
            default: return;
        }
        e.Handled = true;
    }
}
