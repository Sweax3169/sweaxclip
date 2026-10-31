using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.Native;
using SweaxClip.UI;

namespace SweaxClip;

public partial class App : Application
{
    const string InstanceName = "SweaxClip.SingleInstance.6f1c2a";
    Mutex? _instance;
    EventWaitHandle? _activate;

    public ClipStore Store { get; internal set; } = null!;
    public Settings Settings { get; internal set; } = null!;
    CapturePipeline _pipeline = null!;
    OcrService? _ocr;
    HostWindow _host = null!;
    TrayIcon? _tray;
    KeyboardHook? _hook;
    ScreenshotWatcher? _watcher;
    IQuickPanel? _panel;
    LibraryWindow? _library;
    Window? _menuHost;

    readonly DispatcherTimer _changed = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
    readonly DispatcherTimer _trim = new(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer _themeDelay = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
    readonly DispatcherTimer _maintenance = new(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMinutes(10) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToArray();

        if (args.Contains("--demo") || args.Contains("--shots") || args.Contains("--gif") || args.Contains("--appearance") || args.Contains("--classify") || args.Contains("--grab") && args.Contains("--theme"))
        {
            Dev.Demo.Run(this, e.Args);
            return;
        }

        _instance = new Mutex(true, InstanceName, out bool first);
        if (!first)
        {
            // second launch: --toggle opens the quick panel (bind it to a mouse button or macro key), anything else opens the library
            string name = InstanceName + (args.Contains("--toggle") ? ".toggle" : args.Contains("--grab") ? ".grab" : args.Contains("--quit") ? ".quit" : ".activate");
            try { EventWaitHandle.OpenExisting(name).Set(); } catch { }
            Shutdown();
            return;
        }
        if (args.Contains("--quit")) { Shutdown(); return; }
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".activate");
        var toggle = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".toggle");
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".quit");
        var grab = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".grab");
        new Thread(() =>
        {
            var handles = new WaitHandle[] { _activate, toggle, quit, grab };
            while (true)
            {
                int i = WaitHandle.WaitAny(handles);
                if (i == 0) Dispatcher.BeginInvoke(() => ShowLibrary(null));
                else if (i == 1) { IntPtr fg = Win32.GetForegroundWindow(); Dispatcher.BeginInvoke(() => TogglePanel(fg)); }
                else if (i == 3) Dispatcher.BeginInvoke(GrabText);
                else { Dispatcher.BeginInvoke(Quit); return; }
            }
        }) { IsBackground = true, Name = "SweaxClip.Activate" }.Start();

        DispatcherUnhandledException += (_, ex) => { Paths.Log("ui: " + ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Paths.Log("fatal: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Paths.Log("task: " + ex.Exception); ex.SetObserved(); };

        Paths.Init();
        bool firstRun = !File.Exists(Path.Combine(Paths.Data, "settings.json"));
        if (!args.Contains("--no-install") && Installer.RelocateIfNeeded() is { } installed)
        {
            // hand over to the installed copy (release the single-instance lock first)
            _instance.ReleaseMutex();
            _instance.Dispose();
            _instance = null;
            Process.Start(new ProcessStartInfo(installed, string.Join(' ', e.Args.Select(a => $"\"{a}\""))) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installed) });
            Shutdown();
            return;
        }
        Settings = Settings.Load(Paths.Data);
        Loc.Init(Settings.Language);
        ThemeManager.Mode = Settings.ThemeMode;
        ThemeManager.PaletteKey = Settings.Palette;
        ThemeManager.Apply();
        Boot();

        if (firstRun)
        {
            Settings.Save();
            if (Settings.StartWithWindows) AutoStart.Set(true);
            AutoStart.EnsureStartMenuShortcut();
        }
        else if (Settings.StartWithWindows && !AutoStart.IsEnabled()) AutoStart.Set(true); // exe moved

        if (!Settings.ScreenshotsImported && Settings.WatchScreenshots)
        {
            Settings.ScreenshotsImported = true;
            Settings.Save();
            ImportScreenshots(null, null);
        }

        if (!args.Contains("--background")) ShowLibrary(null);
        ScheduleTrim();
    }

    public void Boot(string? dbOverride = null)
    {
        Store = new ClipStore(dbOverride ?? Paths.Db);
        _pipeline = new CapturePipeline(Store, Settings);
        _ocr = new OcrService(Store);
        _pipeline.Ocr = _ocr;
        _ocr.Kick(); // finish anything left from last session

        _host = new HostWindow();
        _host.ClipboardChanged += OnClipboardChanged;
        _host.SessionUnlocked += () => _hook?.Rearm();
        _host.ThemeChanged += () => { _themeDelay.Stop(); _themeDelay.Start(); };
        _themeDelay.Tick += (_, _) => { _themeDelay.Stop(); ThemeManager.Apply(); };

        _tray = new TrayIcon(_host, Loc.T("TrayTip"));
        _tray.LeftClick += () => ShowLibrary(null);
        _tray.RightClick += ShowTrayMenu;

        _hook = new KeyboardHook();
        _hook.Pressed += () =>
        {
            IntPtr fg = Win32.GetForegroundWindow();
            Dispatcher.BeginInvoke(() => TogglePanel(fg), DispatcherPriority.Send);
        };
        _hook.GrabPressed += () => Dispatcher.BeginInvoke(GrabText, DispatcherPriority.Send);
        ApplyHotkey();

        _watcher = new ScreenshotWatcher(_pipeline);
        ApplyWatcher();

        Store.Changed += () => Dispatcher.BeginInvoke(() => { _changed.Stop(); _changed.Start(); });
        _changed.Tick += async (_, _) =>
        {
            _changed.Stop();
            if (_panel is { IsVisible: true }) await _panel.Model.Reload(true);
            if (_library is { IsVisible: true }) await _library.Refresh();
        };
        _ocr.Recognised += (_, _) => Dispatcher.BeginInvoke(() => { _changed.Stop(); _changed.Start(); });
        _ocr.Idle += () => Dispatcher.BeginInvoke(ScheduleTrim);

        _trim.Tick += (_, _) => { _trim.Stop(); Trim(); };
        _maintenance.Tick += (_, _) =>
        {
            _hook?.Rearm();
            Backup.DailyIfDue(Store, Settings);
            if (_panel?.IsVisible != true && _library == null) Trim();
        };
        _maintenance.Start();
        Task.Run(() => Backup.DailyIfDue(Store, Settings));
        // clips saved before the code rules improved get sorted again once (Text → Code only)
        Task.Run(() => { try { int n = Store.ReclassifyText(2, Classifier.Classify); if (n > 0) Paths.Log($"reclassified {n} clips as code"); } catch (Exception ex) { Paths.Log("reclassify: " + ex.Message); } });

        _panel = CreatePanel();
        GCSettings.LatencyMode = GCLatencyMode.Interactive;
    }

    // ── capture ──────────────────────────────────────────────────────────────

    void OnClipboardChanged()
    {
        if (Settings.Paused) return;
        var r = ClipboardReader.Read(_host.Handle, Settings.RespectPrivateFlags, Settings.CaptureText, Settings.CaptureImages, Settings.CaptureFiles, out var clip);
        if (r != ClipboardReader.Result.Ok) return;

        IntPtr fg = Win32.GetForegroundWindow();
        uint pid = 0;
        IntPtr owner = Win32.GetClipboardOwner();
        if (owner != IntPtr.Zero) Win32.GetWindowThreadProcessId(owner, out pid);
        if (pid == 0) Win32.GetWindowThreadProcessId(fg, out pid);
        if (pid == (uint)Environment.ProcessId) return;
        clip.AppPath = Win32.GetProcessPath(pid);
        clip.AppName = AppNames.Friendly(clip.AppPath);
        clip.Title = Win32.GetWindowTitle(fg);
        clip.LikelyScreenshot = _hook != null && Environment.TickCount64 - _hook.LastPrintScreen < 4000;
        _pipeline.Enqueue(clip);
    }

    /// <summary>Freezes the screen and lets you drag over text to copy it.</summary>
    public void GrabText()
    {
        if (_panel is { IsVisible: true }) _panel.HideNow();
        TextGrabber.Start();
    }

    /// <summary>Captures the next shortcut pressed anywhere, for the settings recorder (null: cancelled).</summary>
    public void RecordHotkey(Action<Hotkey?> done)
    {
        if (_hook == null) { done(null); return; }
        _hook.Record(h => Dispatcher.BeginInvoke(() => done(h)));
    }

    public void StopRecordingHotkey() => _hook?.StopRecording();

    public void ApplyHotkey()
    {
        if (_hook != null) _hook.Hotkey = Hotkey.Parse(Settings.Hotkey);
        if (_hook != null)
        {
            var grab = Hotkey.Parse(Settings.GrabHotkey);
            _hook.GrabHotkey = Settings.GrabEnabled && grab != _hook.Hotkey ? grab : null;
        }
    }

    public void ApplyWatcher()
    {
        if (Settings.WatchScreenshots) _watcher?.Start();
        else _watcher?.Stop();
    }

    public void ImportScreenshots(Action<int, int>? progress, Action<int>? done)
    {
        if (_watcher == null) return;
        Action<int, int>? handler = null;
        if (progress != null)
        {
            handler = (n, t) => progress(n, t);
            _watcher.ImportProgress += handler;
        }
        _ = Task.Run(() => _watcher.ImportExisting(added =>
        {
            if (handler != null) _watcher.ImportProgress -= handler;
            done?.Invoke(added);
            Dispatcher.BeginInvoke(ScheduleTrim);
        }));
    }

    public void SetPaused(bool paused)
    {
        Settings.Paused = paused;
        Settings.Save();
        _tray?.SetTip(paused ? $"{Loc.T("TrayTip")} — {Loc.T("Paused")}" : Loc.T("TrayTip"));
    }

    // ── windows ──────────────────────────────────────────────────────────────

    IQuickPanel CreatePanel() => Settings.PanelStyle == "deck" ? new DeckPanel(this) : new QuickPanel(this);

    /// <summary>Swaps the Win+V panel for the style chosen in Settings.</summary>
    public void ApplyPanelStyle()
    {
        if (_panel == null) return;
        _panel.HideNow();
        _panel.Close();
        _panel = CreatePanel();
    }

    void TogglePanel(IntPtr foreground)
    {
        if (_panel == null) return;
        if (_library is { IsActive: true } && !_panel.IsVisible) foreground = new System.Windows.Interop.WindowInteropHelper(_library).Handle;
        _panel.Toggle(foreground);
    }

    public async void ShowLibrary(long? focusId)
    {
        if (_library == null)
        {
            _library = new LibraryWindow(this);
            _library.Closed += (_, _) => { _library = null; ScheduleTrim(); };
            _library.Show();
        }
        else
        {
            if (_library.WindowState == WindowState.Minimized) _library.WindowState = WindowState.Normal;
            _library.Show();
        }
        _library.Activate();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(_library).Handle);
        if (focusId != null) await _library.FocusClip(focusId);
    }

    void ShowTrayMenu()
    {
        _menuHost ??= new Window
        {
            Width = 1, Height = 1, Left = -100, Top = -100, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, Topmost = true,
        };
        _menuHost.Show();
        _menuHost.Activate();
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = _menuHost };
        MenuItem Item(string text, string glyph, Action act)
        {
            var m = new MenuItem { Header = text, Tag = glyph };
            m.Click += (_, _) => act();
            return m;
        }
        menu.Items.Add(Item(Loc.T("OpenLibrary"), Glyphs.History, () => ShowLibrary(null)));
        var grabItem = Item(Loc.T("GrabText"), "\uE8D2", GrabText);
        if (Settings.GrabEnabled) grabItem.InputGestureText = Hotkey.Parse(Settings.GrabHotkey).ToString();
        menu.Items.Add(grabItem);
        menu.Items.Add(Item(Settings.Paused ? Loc.T("ResumeCapture") : Loc.T("PauseCapture"), Settings.Paused ? Glyphs.Play : Glyphs.Pause, () => SetPaused(!Settings.Paused)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Quit"), Glyphs.Close, Quit));
        menu.Closed += (_, _) => _menuHost?.Hide();
        menu.IsOpen = true;
    }

    public void ScheduleTrim()
    {
        _trim.Stop();
        _trim.Start();
    }

    /// <summary>Hand memory back to Windows while SweaxClip sits in the tray.</summary>
    void Trim()
    {
        if (_panel?.IsVisible == true || _library != null) return;
        ThumbLoader.Clear();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        Win32.TrimWorkingSet();
    }

    public void Quit()
    {
        _hook?.Dispose();
        _watcher?.Dispose();
        _tray?.Dispose();
        _host?.Dispose();
        _pipeline?.Dispose();
        Store?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); } catch { }
        _instance?.Dispose();
        base.OnExit(e);
    }

    // ── shell helpers ────────────────────────────────────────────────────────

    public static void Shell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Paths.Log("shell: " + ex.Message); }
    }

    public void OpenItem(ClipItem c)
    {
        switch (c.Kind)
        {
            case ClipKind.Link: Shell(c.LinkUrl); break;
            case ClipKind.Files: if (c.Files.FirstOrDefault() is { } f) Shell(f); break;
            case ClipKind.Image or ClipKind.Screenshot: if (c.Blob != null) Shell(Paths.Abs(c.Blob)); break;
        }
    }

    public void ShowInFolder(ClipItem c)
    {
        string? path = c.Kind == ClipKind.Files ? c.Files.FirstOrDefault() : c.Blob != null ? Paths.Abs(c.Blob) : null;
        if (path == null) return;
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    public void SaveImageAs(ClipItem c)
    {
        if (c.Blob == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"sweaxclip-{TimeText.Local(c.Created):yyyyMMdd-HHmmss}.png",
            Filter = "PNG (*.png)|*.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog() == true)
        {
            try { File.Copy(Paths.Abs(c.Blob), dlg.FileName, true); } catch (Exception ex) { Paths.Log("save: " + ex.Message); }
        }
    }

    public static void DeleteFiles(IEnumerable<string> rel)
    {
        foreach (var r in rel)
        {
            try { File.Delete(Paths.Abs(r)); } catch { }
        }
    }
}

/// <summary>Human names for executables ("chrome.exe" → "Google Chrome").</summary>
public static class AppNames
{
    static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? Friendly(string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(exe, out var n)) return n;
            string name = Path.GetFileNameWithoutExtension(exe);
            try
            {
                var fv = FileVersionInfo.GetVersionInfo(exe);
                string? d = fv.FileDescription?.Trim();
                if (!string.IsNullOrEmpty(d) && d.Length <= 40 && !d.Contains(".exe", StringComparison.OrdinalIgnoreCase)) name = d;
                else if (!string.IsNullOrEmpty(fv.ProductName) && fv.ProductName.Length <= 40) name = fv.ProductName.Trim();
            }
            catch { }
            Cache[exe] = name;
            return name;
        }
    }
}
