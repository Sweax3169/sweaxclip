using System.Collections.Concurrent;
using System.IO;

namespace SweaxClip.Core;

/// <summary>
/// Turns raw clipboard captures into archive rows on one background STA thread
/// (hashing, PNG encoding and thumbnails never touch the UI thread).
/// </summary>
public sealed class CapturePipeline : IDisposable
{
    readonly ClipStore _store;
    readonly Settings _settings;
    readonly BlockingCollection<Action> _queue = new();
    readonly Thread _thread;

    public event Action<long>? Captured;
    public OcrService? Ocr { get; set; }

    static readonly HashSet<string> ScreenshotApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "SnippingTool", "ScreenClippingHost", "ScreenSketch", "ShareX", "Greenshot", "Lightshot", "Flameshot", "Snagit32", "SnagitEditor", "PicPick", "Gyazo",
    };

    public CapturePipeline(ClipStore store, Settings settings)
    {
        _store = store;
        _settings = settings;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SweaxClip.Capture", Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Enqueue(CapturedClip clip) => _queue.Add(() => Process(clip));
    public void Enqueue(Action work) => _queue.Add(work);

    void Loop()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try { work(); }
            catch (Exception ex) { Paths.Log("pipeline: " + ex); }
        }
    }

    void Process(CapturedClip c)
    {
        string app = c.AppName ?? "";
        if (c.AppPath != null && _settings.IgnoredApps.Any(a => string.Equals(a.Trim(), Path.GetFileName(c.AppPath), StringComparison.OrdinalIgnoreCase)))
            return;

        long? id = null;
        if (c.Files is { Length: > 0 }) id = ProcessFiles(c);
        else if (c.HasImage) id = ProcessImage(c);
        else if (!string.IsNullOrWhiteSpace(c.Text)) id = ProcessText(c);

        if (id is long v) Captured?.Invoke(v);
    }

    long ProcessText(CapturedClip c)
    {
        string text = c.Text!;
        string hash = ImageTools.TextHash("T:", text);
        if (_store.Touch(hash, c.When) is long existing) return existing;
        var item = NewItem(c, Classifier.Classify(text), hash);
        item.Text = text;
        item.Size = System.Text.Encoding.UTF8.GetByteCount(text);
        // keep rich formats only when they add something and stay reasonable
        string? html = c.Html is { Length: < 4_000_000 } ? c.Html : null;
        string? rtf = c.Rtf is { Length: < 4_000_000 } ? c.Rtf : null;
        return _store.Insert(item, html, rtf, 0);
    }

    long? ProcessImage(CapturedClip c)
    {
        var bmp = ImageTools.Decode(c.Png, c.Dib);
        if (bmp == null) return null;
        string hash = ImageTools.PixelHash(bmp);
        if (_store.Touch(hash, c.When) is long existing) return existing;

        bool shot = c.LikelyScreenshot
                    || (c.AppPath != null && ScreenshotApps.Contains(Path.GetFileNameWithoutExtension(c.AppPath)))
                    || Screens.MatchesMonitor(bmp.PixelWidth, bmp.PixelHeight);
        return StoreImage(c, bmp, c.Png, hash, shot ? ClipKind.Screenshot : ClipKind.Image);
    }

    long StoreImage(CapturedClip c, System.Windows.Media.Imaging.BitmapSource bmp, byte[]? png, string hash, ClipKind kind)
    {
        var (rel, size) = ImageTools.SaveOriginal(bmp, png, hash);
        var item = NewItem(c, kind, hash);
        item.Blob = rel;
        item.Width = bmp.PixelWidth;
        item.Height = bmp.PixelHeight;
        item.Size = size;
        item.Thumb = ImageTools.SaveThumb(Paths.Abs(rel), hash);
        bool ocr = _settings.Ocr && Ocr != null;
        long id = _store.Insert(item, null, null, ocr ? 1 : 0);
        if (ocr) Ocr!.Kick();
        return id;
    }

    long ProcessFiles(CapturedClip c)
    {
        string text = string.Join("\n", c.Files!);
        string hash = ImageTools.TextHash("F:", text);
        if (_store.Touch(hash, c.When) is long existing) return existing;
        var item = NewItem(c, ClipKind.Files, hash);
        item.Text = text;
        long total = 0;
        foreach (var f in c.Files!.Take(200))
        {
            try { if (File.Exists(f)) total += new FileInfo(f).Length; } catch { }
        }
        item.Size = total;
        string first = c.Files![0];
        if (c.Files.Length == 1 && ImageTools.ImageExtensions.Contains(Path.GetExtension(first)) && File.Exists(first))
        {
            item.Thumb = ImageTools.SaveThumb(first, hash);
            (item.Width, item.Height) = ImageTools.ProbeSize(first);
        }
        return _store.Insert(item, null, null, 0);
    }

    /// <summary>Imports an image file (e.g. from the Screenshots folder) as a screenshot.</summary>
    public long? ImportImageFile(string file, ClipKind kind, long when)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(file); } catch { return null; }
        var bmp = ImageTools.Decode(ImageTools.IsPng(bytes) ? bytes : null, null);
        if (bmp == null)
        {
            try
            {
                var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(new MemoryStream(bytes), System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var f = dec.Frames[0];
                f.Freeze();
                bmp = f;
            }
            catch { return null; }
        }
        string hash = ImageTools.PixelHash(bmp);
        if (_store.HasHash(hash)) return null;
        var c = new CapturedClip { When = when, AppName = "Screenshots", Title = Path.GetFileName(file) };
        return StoreImage(c, bmp, ImageTools.IsPng(bytes) ? bytes : null, hash, kind);
    }

    static ClipItem NewItem(CapturedClip c, ClipKind kind, string hash) => new()
    {
        Kind = kind,
        Hash = hash,
        App = c.AppName,
        AppPath = c.AppPath,
        Title = c.Title is { Length: > 300 } t ? t[..300] : c.Title,
        Created = c.When,
        Used = c.When,
    };

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(3000);
    }
}

internal static class Screens
{
    static (int w, int h)[]? _sizes;
    public static void Refresh() => _sizes = null;

    public static bool MatchesMonitor(int w, int h)
    {
        _sizes ??= ReadSizes();
        return _sizes.Any(s => (s.w == w && s.h == h) || (s.w * 2 == w && s.h == h) || (s.w == w && s.h * 2 == h));
    }

    static (int, int)[] ReadSizes()
    {
        var list = new List<(int, int)>();
        try
        {
            // System.Windows.Forms is not referenced; enumerate monitors through WPF's virtual screen as a fallback.
            int vw = (int)System.Windows.SystemParameters.VirtualScreenWidth, vh = (int)System.Windows.SystemParameters.VirtualScreenHeight;
            foreach (var m in MonitorList.All()) list.Add((m.Width, m.Height));
            list.Add((vw, vh));
        }
        catch { }
        return [.. list];
    }
}

internal static class MonitorList
{
    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Native.Win32.RECT rect, IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    public static List<Native.Win32.RECT> All()
    {
        var list = new List<Native.Win32.RECT>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref Native.Win32.RECT r, IntPtr _) => { list.Add(r); return true; }, IntPtr.Zero);
        return list;
    }
}
