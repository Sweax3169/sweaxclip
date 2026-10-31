using System.IO;
using SweaxClip.Native;

namespace SweaxClip.Core;

/// <summary>Follows the Windows "Screenshots" folder so Win+PrtScn / Snipping Tool saves are archived too.</summary>
public sealed class ScreenshotWatcher : IDisposable
{
    readonly CapturePipeline _pipeline;
    FileSystemWatcher? _fsw;
    readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public event Action<int, int>? ImportProgress;

    public ScreenshotWatcher(CapturePipeline pipeline) => _pipeline = pipeline;

    public static string? Folder
    {
        get
        {
            string? p = Win32.KnownFolder(Win32.FOLDERID_Screenshots);
            if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            return Directory.Exists(fallback) ? fallback : null;
        }
    }

    public void Start()
    {
        string? dir = Folder;
        if (dir == null || _fsw != null) return;
        _fsw = new FileSystemWatcher(dir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, InternalBufferSize = 16 * 1024 };
        _fsw.Created += (_, e) => OnFile(e.FullPath);
        _fsw.Renamed += (_, e) => OnFile(e.FullPath);
        _fsw.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _fsw?.Dispose();
        _fsw = null;
    }

    void OnFile(string path)
    {
        if (!ImageTools.ImageExtensions.Contains(Path.GetExtension(path))) return;
        lock (_seen) if (!_seen.Add(path)) return;
        _ = Task.Run(async () =>
        {
            // wait until the writer is done with the file
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(250);
                try { using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); break; } catch { }
            }
            long when = TimeText.Now;
            _pipeline.Enqueue(() => _pipeline.ImportImageFile(path, ClipKind.Screenshot, when));
        });
    }

    /// <summary>One-time import of the screenshots already on disk, oldest first, keeping their real dates.</summary>
    public void ImportExisting(Action<int>? done = null)
    {
        string? dir = Folder;
        if (dir == null) { done?.Invoke(0); return; }
        var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => ImageTools.ImageExtensions.Contains(Path.GetExtension(f)))
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();
        int total = files.Count, n = 0, added = 0;
        foreach (var f in files)
        {
            var fi = f;
            _pipeline.Enqueue(() =>
            {
                long when = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                if (_pipeline.ImportImageFile(fi.FullName, ClipKind.Screenshot, when) != null) added++;
                n++;
                ImportProgress?.Invoke(n, total);
                if (n == total) done?.Invoke(added);
            });
        }
        if (total == 0) done?.Invoke(0);
    }

    public void Dispose() => Stop();
}
