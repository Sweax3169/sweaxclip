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
    WaitHandle? _instance;
    bool _deleteTaskOnQuit;

    public ClipStore Store { get; internal set; } = null!;
    public Settings Settings { get; internal set; } = null!;
    CapturePipeline _pipeline = null!;
    OcrService? _ocr;
    HostWindow _host = null!;
    TrayIcon? _tray;
    KeyboardHook? _hook;
    ScreenshotWatcher? _watcher;
    PanelHost? _panel;
    LibraryWindow? _library;
    Window? _menuHost;
    long _keepOwnCopyUntil;
    ClipboardWatcher? _clipboard;
    bool _trimPending;

    readonly DispatcherTimer _changed = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
    readonly DispatcherTimer _idle = new(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer _themeDelay = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
    readonly DispatcherTimer _maintenance = new(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMinutes(10) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToArray();

        if (args.Contains("--demo") || args.Contains("--caret-probe") || args.Contains("--task-xml") || args.Contains("--bench") || args.Contains("--shots") || args.Contains("--gif") || args.Contains("--appearance") || args.Contains("--classify") || args.Contains("--grab") && args.Contains("--theme"))
        {
            Dev.Demo.Run(this, e.Args);
            return;
        }

        _instance = Shared.Mutex(InstanceName, out bool first);
        // --replace (the sign-in task): take over from a copy started with the other kind of rights
        if (!first && args.Contains("--replace") && _instance != null && Shared.TakeOver(_instance, InstanceName + ".quit")) first = true;
        if (!first)
        {
            // started at sign-in while a copy already runs: nothing to do
            if (args.Contains("--background")) { Shutdown(); return; }
            // second launch: --toggle opens the quick panel (bind it to a mouse button or macro key), anything else opens the library
            string name = InstanceName + (args.Contains("--toggle") ? ".toggle" : args.Contains("--grab") ? ".grab" : args.Contains("--quit") ? ".quit" : ".activate");
            try { EventWaitHandle.OpenExisting(name).Set(); } catch { }
            Shutdown();
            return;
        }
        if (args.Contains("--quit")) { Shutdown(); return; }
        var activate = Shared.Event(InstanceName + ".activate");
        var toggle = Shared.Event(InstanceName + ".toggle");
        var quit = Shared.Event(InstanceName + ".quit");
        var grab = Shared.Event(InstanceName + ".grab");
        new Thread(() =>
        {
            var handles = new WaitHandle[] { activate, toggle, quit, grab };
            while (true)
            {
                int i = WaitHandle.WaitAny(handles);
                if (i == 0) Dispatcher.BeginInvoke(() => ShowLibrary(null));
                else if (i == 1) _panel?.Toggle(Win32.GetForegroundWindow(), Environment.TickCount64);
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
            if (_instance != null) Shared.Release(_instance);
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
        else if (Settings.RunAsAdmin) KeepAdminRights(args.Contains("--replace"));
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
        _clipboard = new ClipboardWatcher(ReadClipboard);
        _host.ClipboardChanged += _clipboard.Signal;
        _host.SessionUnlocked += () => _hook?.Rearm();
        _host.ThemeChanged += () => { _themeDelay.Stop(); _themeDelay.Start(); };
        _themeDelay.Tick += (_, _) => { _themeDelay.Stop(); ThemeManager.Apply(); };

        _tray = new TrayIcon(_host, Loc.T("TrayTip"));
        _tray.LeftClick += () => ShowLibrary(null);
        _tray.RightClick += ShowTrayMenu;

        _hook = new KeyboardHook();
        // straight to the panel's own thread: nothing on the main thread can hold Win+V up
        _hook.Pressed += () => _panel?.Toggle(Win32.GetForegroundWindow(), Environment.TickCount64);
        _hook.GrabPressed += () => Dispatcher.BeginInvoke(GrabText, DispatcherPriority.Send);
        _hook.Key += k => _panel?.Key(k, _hook.Handled);
        _hook.MouseDown += pt => _panel?.MouseDown(pt);
        _hook.CaptureLost += () => { Paths.Log("panel stopped answering keys"); _panel?.Hide(); };
        ApplyHotkey();

        _watcher = new ScreenshotWatcher(_pipeline);
        ApplyWatcher();

        Store.Changed += () => Dispatcher.BeginInvoke(() => { _changed.Stop(); _changed.Start(); });
        _changed.Tick += async (_, _) =>
        {
            _changed.Stop();
            // the panel's rows are kept current while it is hidden, so Win+V never has to wait for the archive
            _panel?.Refresh();
            if (_library is { IsVisible: true }) await _library.Refresh();
        };
        _ocr.Recognised += (_, _) => Dispatcher.BeginInvoke(() => { _changed.Stop(); _changed.Start(); });
        _ocr.Idle += () => Dispatcher.BeginInvoke(ScheduleTrim);

        _idle.Tick += (_, _) =>
        {
            // memory goes back to Windows only while the user is away; trimming right after the panel closed made the
            // next Win+V page everything back in, and a full collection while typing stalls the keyboard hook
            if (_trimPending && _panel?.Shown != true && _library == null && Win32.IdleMilliseconds() > 10 * 60_000)
            {
                _trimPending = false;
                Trim();
            }
        };
        _idle.Start();
        _maintenance.Tick += (_, _) =>
        {
            _hook?.Rearm();
            Task.Run(() => Backup.DailyIfDue(Store, Settings));
            ScheduleTrim();
        };
        _maintenance.Start();
        Task.Run(() => Backup.DailyIfDue(Store, Settings));
        // clips saved before the code rules improved get sorted again once (Text → Code only)
        Task.Run(() => { try { int n = Store.ReclassifyText(2, Classifier.Classify); if (n > 0) Paths.Log($"reclassified {n} clips as code"); } catch (Exception ex) { Paths.Log("reclassify: " + ex.Message); } });

        _panel = new PanelHost(this);
        ThemeManager.Changed += () => _panel?.ApplyTheme();
        GCSettings.LatencyMode = GCLatencyMode.Interactive;
        WarmPanelSoon();
        new Watchdog(Dispatcher, "UI");
    }

    /// <summary>Lays the panel out once off-screen a few seconds after start, so the first Win+V opens as fast as the rest.</summary>
    void WarmPanelSoon()
    {
        var once = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(4) };
        once.Tick += async (_, _) =>
        {
            once.Stop();
            try { if (_panel != null) await _panel.Warm(); }
            catch (Exception ex) { Paths.Log("warm: " + ex.Message); }
            StayResident();
        };
        once.Start();
    }

    /// <summary>While the panel is open its keys come from the hook, and a click anywhere else closes it.</summary>
    public void CaptureKeys(bool on)
    {
        if (_hook != null) _hook.Capture = on;
    }




    // ── capture ──────────────────────────────────────────────────────────────

    /// <summary>Runs on the clipboard thread (see <see cref="ClipboardWatcher"/>), never on the UI thread.</summary>
    void ReadClipboard()
    {
        if (Settings.Paused) return;
        // a clip SweaxClip put back is being pasted right now: don't open the clipboard under the app that is reading it
        if (Win32.IsClipboardFormatAvailable(ClipboardReader.FmtMarker)) return;
        var r = ClipboardReader.Read(IntPtr.Zero, Settings.RespectPrivateFlags, Settings.CaptureText, Settings.CaptureImages, Settings.CaptureFiles, out var clip);
        if (r != ClipboardReader.Result.Ok) return;

        IntPtr fg = Win32.GetForegroundWindow();
        uint pid = 0;
        IntPtr owner = Win32.GetClipboardOwner();
        if (owner != IntPtr.Zero) Win32.GetWindowThreadProcessId(owner, out pid);
        if (pid == 0) Win32.GetWindowThreadProcessId(fg, out pid);
        bool fromScreen = false;
        if (pid == (uint)Environment.ProcessId)
        {
            // our own writes are clips being pasted back, except the text copied from the screen
            if (Environment.TickCount64 > Interlocked.Exchange(ref _keepOwnCopyUntil, 0)) return;
            fromScreen = true;
        }
        clip.AppPath = Win32.GetProcessPath(pid);
        clip.AppName = fromScreen ? Loc.T("GrabSource") : AppNames.Friendly(clip.AppPath);
        clip.Title = Win32.GetWindowTitle(fg);
        clip.LikelyScreenshot = _hook != null && Environment.TickCount64 - _hook.LastPrintScreen < 4000;
        _pipeline.Enqueue(clip);
    }

    /// <summary>Freezes the screen and lets you drag over text to copy it.</summary>
    public void GrabText()
    {
        _panel?.Hide();
        TextGrabber.Start();
    }

    /// <summary>The next clipboard change from this process is text read off the screen: keep it in the history.</summary>
    public void KeepNextOwnCopy() => Interlocked.Exchange(ref _keepOwnCopyUntil, Environment.TickCount64 + 2000);

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


    /// <summary>Swaps the Win+V panel for the style chosen in Settings.</summary>
    public void ApplyPanelStyle() => _panel?.Restyle();


    public async void ShowLibrary(long? focusId)
    {
        // the panel asks for the library from its own thread
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(() => ShowLibrary(focusId)); return; }
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

    /// <summary>Memory can go back to Windows the next time the user is away (see the idle timer).</summary>
    public void ScheduleTrim() => _trimPending = true;

    /// <summary>
    /// Tidies the managed heap while the user is away. The panel's own memory — its rows, pictures and code — stays put
    /// (see <see cref="StayResident"/>): emptying it made the next Win+V read it all back from disk, seconds on a busy PC.
    /// </summary>
    void Trim()
    {
        if (_panel?.Shown == true || _library != null) return;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
    }

    /// <summary>
    /// Keeps SweaxClip in memory like a part of Windows. A hard minimum working set stops Windows from paging it out when
    /// other programs (builds, renders, emulators) want memory; otherwise the first Win+V after that has to read
    /// everything back from disk. Normal-and-above priority keeps it quick while those programs use every core — the
    /// panel never takes focus, so it doesn't get the foreground boost windows normally get.
    /// </summary>
    static void StayResident()
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
        if (!Win32.SetProcessWorkingSetSizeEx(Win32.GetCurrentProcess(), 160u << 20, 1024u << 20, Win32.QUOTA_LIMITS_HARDWS_MIN_ENABLE | Win32.QUOTA_LIMITS_HARDWS_MAX_DISABLE))
            Paths.Log("could not keep SweaxClip in memory: error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    /// <summary>
    /// With "work over administrator windows" on, the sign-in task starts SweaxClip (the Run entry would start a second,
    /// ordinary copy). A copy started without administrator rights — after an update, or by hand — asks the task to
    /// start the administrator copy, which takes over.
    /// </summary>
    void KeepAdminRights(bool startedByTask)
    {
        AutoStart.Set(false);
        if (Elevation.IsElevated || startedByTask) return;
        Task.Run(() =>
        {
            if (Elevation.RunTask()) return;
            Paths.Log("the administrator sign-in task is missing; running without administrator rights");
            Dispatcher.BeginInvoke(() =>
            {
                Settings.RunAsAdmin = false;
                Settings.Save();
                if (Settings.StartWithWindows) AutoStart.Set(true);
            });
        });
    }

    /// <summary>
    /// Turns "work over administrator windows" on or off. On: the sign-in task is made with administrator rights (Windows
    /// asks once) and started, and the administrator copy takes over from this one. Off: the task is made ordinary and
    /// started the same way, and is removed as this copy quits. Returns a note for Settings when nothing changed.
    /// </summary>
    public async Task<string?> SetRunAsAdmin(bool on)
    {
        if (on == Settings.RunAsAdmin && (!on || Elevation.IsElevated)) return null;
        if (!await Elevation.RegisterTask(on)) return Loc.T(on ? "AdminDeclined" : "AdminFailed");
        Settings.RunAsAdmin = on;
        if (on) Settings.StartWithWindows = true;
        Settings.Save();
        if (on) AutoStart.Set(false);
        else
        {
            AutoStart.Set(Settings.StartWithWindows);
            _deleteTaskOnQuit = true;
        }
        if (!Elevation.RunTask())
        {
            if (!on) { Elevation.DeleteTask(); _deleteTaskOnQuit = false; }
            return Loc.T("AdminNextSignIn");
        }
        return null; // the new copy asks this one to quit in a moment
    }

    public void Quit()
    {
        _clipboard?.Dispose();
        _panel?.Shutdown();
        if (_deleteTaskOnQuit) Elevation.DeleteTask();
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
        // with administrator rights, anything opened here would get them too (a browser running as administrator):
        // Explorer opens it as the user instead
        if (Elevation.IsElevated)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = false }); }
            catch (Exception ex) { Paths.Log("shell: " + ex.Message); }
            return;
        }
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
