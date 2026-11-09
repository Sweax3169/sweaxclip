using System.Windows.Threading;

namespace SweaxClip.Core;

/// <summary>
/// Pings a UI thread four times a second and notes in the log whenever it stopped answering for more than a second,
/// with what it was doing. Win+V can only be as quick as the thread that opens the panel, so any stall there is worth
/// knowing about.
/// </summary>
public sealed class Watchdog
{
    /// <summary>What the main UI thread is busy with, set around the few things that can take long.</summary>
    public static volatile string Doing = "";

    readonly Dispatcher _ui;
    readonly string _name;
    long _sentAt;

    public Watchdog(Dispatcher ui, string name)
    {
        _ui = ui;
        _name = name;
        new Thread(Loop) { IsBackground = true, Name = "SweaxClip.Watchdog", Priority = ThreadPriority.BelowNormal }.Start();
    }

    void Loop()
    {
        bool warned = false;
        while (!_ui.HasShutdownStarted)
        {
            Thread.Sleep(250);
            long sent = Interlocked.Read(ref _sentAt);
            if (sent == 0)
            {
                long now = Environment.TickCount64;
                Interlocked.Exchange(ref _sentAt, now);
                warned = false;
                _ui.BeginInvoke(DispatcherPriority.Send, () =>
                {
                    long waited = Environment.TickCount64 - now;
                    Interlocked.Exchange(ref _sentAt, 0);
                    if (waited > 1000) Paths.Log($"{_name} thread did not answer for {waited} ms{(Doing.Length > 0 ? " while " + Doing : "")}");
                });
            }
            else if (!warned && Environment.TickCount64 - sent > 10_000)
            {
                warned = true;
                Paths.Log($"{_name} thread has not answered for 10 s{(Doing.Length > 0 ? " — " + Doing : "")}");
            }
        }
    }
}
