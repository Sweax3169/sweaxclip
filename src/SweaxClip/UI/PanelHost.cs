using System.Windows;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// Runs the Win+V panel on a UI thread of its own, so nothing else SweaxClip does — the library, a slow clipboard, the
/// archive, a long layout — can hold Win+V up. The keyboard hook posts straight to this thread; the main thread is not
/// in the way. Everything here is safe to call from any thread.
/// </summary>
public sealed class PanelHost
{
    readonly App _app;
    readonly Dispatcher _ui;
    IQuickPanel? _panel;
    volatile bool _shown;
    long _target;
    Win32.WinEventProc? _onForeground;
    readonly HashSet<string> _adminNoted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The panel is on screen (kept by the panel thread, readable anywhere).</summary>
    public bool Shown => _shown;

    public PanelHost(App app)
    {
        _app = app;
        Dispatcher? ui = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            ui = Dispatcher.CurrentDispatcher;
            try
            {
                ThreadResources.Init();
                _panel = Create();
                // the panel closes when another window comes to the front, as a focused window would on losing focus
                _onForeground = (_, _, hwnd, _, _, _, _) => OnForeground(hwnd);
                Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground, 0, 0, 0);
            }
            catch (Exception ex) { Paths.Log("panel thread: " + ex); }
            finally { ready.Set(); }
            Dispatcher.Run();
        }) { IsBackground = true, Name = "SweaxClip.Panel", Priority = ThreadPriority.AboveNormal };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        _ui = ui!;
        _ui.UnhandledException += (_, e) => { Paths.Log("panel: " + e.Exception); e.Handled = true; };
        new Watchdog(_ui, "panel");
    }

    IQuickPanel Create()
    {
        IQuickPanel p = _app.Settings.PanelStyle == "deck" ? new DeckPanel(_app) : new QuickPanel(_app);
        var w = (Window)p;
        w.IsVisibleChanged += (_, _) =>
        {
            _shown = w.IsVisible;
            Interlocked.Exchange(ref _target, (long)p.Target);
        };
        return p;
    }

    void Post(Action work, DispatcherPriority priority = DispatcherPriority.Normal) => _ui.BeginInvoke(priority, work);

    /// <summary>Win+V: open over <paramref name="foreground"/>, or close.</summary>
    public void Toggle(IntPtr foreground, long pressed) => Post(() => _panel?.Toggle(foreground, pressed), DispatcherPriority.Send);

    public void Hide() => Post(() => _panel?.HideNow(), DispatcherPriority.Send);

    /// <summary>A key for the open panel; <paramref name="done"/> tells the hook it was taken.</summary>
    public void Key(CapturedKey key, Action done) => Post(() =>
    {
        try { _panel?.OnKey(key); }
        finally { done(); }
    }, DispatcherPriority.Send);

    /// <summary>A click while the panel is open: anywhere outside SweaxClip's windows closes it.</summary>
    internal void MouseDown(Win32.POINT pt) => Post(() =>
    {
        if (_panel is not { IsVisible: true } panel) return;
        IntPtr hit = Win32.GetAncestor(Win32.WindowFromPoint(pt), Win32.GA_ROOT);
        if (!Layers.IsOurs(hit)) panel.HideNow(); // menus and the panel itself are ours
    });

    /// <summary>The archive changed: bring the rows up to date now, so the next Win+V shows them at once.</summary>
    public void Refresh() => Post(async () =>
    {
        if (_panel != null) await _panel.Model.Reload(true);
    }, DispatcherPriority.Background);

    public Task Warm() => _ui.InvokeAsync(async () =>
    {
        if (_panel != null) await _panel.Warm();
    }).Task.Unwrap();

    /// <summary>The panel style changed in Settings: build the other kind.</summary>
    public void Restyle() => Post(async () =>
    {
        _panel?.HideNow();
        _panel?.Close();
        _panel = Create();
        await _panel.Warm();
    });

    /// <summary>The palette or light/dark mode changed on the main thread.</summary>
    public void ApplyTheme() => Post(ThreadResources.ApplyTheme);

    public void Shutdown() => _ui.BeginInvokeShutdown(DispatcherPriority.Send);

    void OnForeground(IntPtr hwnd)
    {
        NoteAdminWindow(hwnd);
        if (_panel is not { IsVisible: true } panel || hwnd == panel.Target) return;
        if (!Layers.IsOurs(hwnd)) panel.HideNow();
    }

    /// <summary>Notes (once per app) when an administrator window comes to the front: Win+V can't reach SweaxClip there.</summary>
    void NoteAdminWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _app.Settings.RunAsAdmin || !Layers.IsAdminAboveUs(hwnd)) return;
        string what = Layers.Describe(hwnd);
        if (_adminNoted.Add(what.Split(' ')[0]))
            Paths.Log($"administrator window in front ({what}): Windows keeps its keys from ordinary apps, so Win+V there opens Windows' own panel (Settings → General → Work over administrator windows)");
    }
}
