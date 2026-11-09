using System.Diagnostics;

namespace SweaxClip.Core;

/// <summary>
/// Reads the clipboard on a thread of its own. Many apps put a copy on the clipboard only as a promise and make it when
/// someone reads it (delayed rendering): Java apps such as Android Studio, Office, games. A busy app keeps the reader
/// waiting — Windows allows it up to 30 seconds. On the UI thread that froze everything, Win+V included, which is why the
/// panel sometimes opened half a minute late.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    readonly AutoResetEvent _changed = new(false);
    readonly Thread _thread;
    readonly Action _read;
    volatile bool _stop;
    long _readingSince;

    public ClipboardWatcher(Action read)
    {
        _read = read;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SweaxClip.Clipboard" };
        _thread.Start();
    }

    /// <summary>The clipboard changed. Changes that arrive while a read is under way are read once, after it.</summary>
    public void Signal() => _changed.Set();

    /// <summary>How long the current read has been waiting for the app that made the copy (0 when idle).</summary>
    public long ReadingFor => Interlocked.Read(ref _readingSince) is long t and > 0 ? Environment.TickCount64 - t : 0;

    void Loop()
    {
        while (_changed.WaitOne() && !_stop)
        {
            Interlocked.Exchange(ref _readingSince, Environment.TickCount64);
            var sw = Stopwatch.StartNew();
            try { _read(); }
            catch (Exception ex) { Paths.Log("clipboard: " + ex.Message); }
            finally { Interlocked.Exchange(ref _readingSince, 0); }
            if (sw.ElapsedMilliseconds > 1500) Paths.Log($"reading the clipboard took {sw.ElapsedMilliseconds} ms: the app that copied made its copy only when asked (on its own thread now, nothing waits for it)");
        }
    }

    public void Dispose()
    {
        _stop = true;
        _changed.Set();
    }
}
