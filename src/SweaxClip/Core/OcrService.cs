using System.IO;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SweaxClip.Core;

/// <summary>
/// Reads text inside images with the OCR engine that ships with Windows — fully offline.
/// Works one image at a time at low priority, so screenshots become searchable by their words.
/// </summary>
public sealed class OcrService
{
    readonly ClipStore _store;
    readonly SemaphoreSlim _signal = new(0);
    int _running;
    OcrEngine? _engine;
    bool _unavailable;

    public event Action<long, string?>? Recognised;
    /// <summary>Raised when the queue is drained and the engine has been released.</summary>
    public event Action? Idle;

    public OcrService(ClipStore store) => _store = store;

    public void Kick()
    {
        if (_unavailable) return;
        if (Interlocked.Exchange(ref _running, 1) == 1) { _signal.Release(); return; }
        Task.Run(Worker);
    }

    async Task Worker()
    {
        try
        {
            await Task.Delay(1500); // let the copy-paste burst finish first
            while (true)
            {
                var batch = _store.PendingOcr(8);
                if (batch.Count == 0)
                {
                    if (!await _signal.WaitAsync(TimeSpan.FromSeconds(20))) break;
                    continue;
                }
                foreach (var (id, blob) in batch)
                {
                    string? text = null;
                    int state = 3;
                    try
                    {
                        text = await Recognise(Paths.Abs(blob));
                        state = 2;
                    }
                    catch (Exception ex) { Paths.Log($"ocr {id}: {ex.Message}"); }
                    if (_unavailable) return;
                    _store.SetOcr(id, string.IsNullOrWhiteSpace(text) ? null : text, state);
                    Recognised?.Invoke(id, text);
                    await Task.Delay(120);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            _engine = null;
            Idle?.Invoke();
        }
    }

    static OcrEngine? _screenEngine;

    /// <summary>
    /// Reads the text in a picture taken from the screen. The picture is first made easy to read (see
    /// <see cref="ForReading"/>) and scaled so its lines of text are about 36 pixels tall: the engine misreads
    /// small interface text, and enlarging text that is already big makes it worse.
    /// </summary>
    public static async Task<string> ReadScreenAsync(System.Windows.Media.Imaging.BitmapSource image, double? forceScale = null, bool prepare = true)
    {
        _screenEngine ??= OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        if (_screenEngine == null) return "";

        var (clean, lineHeight) = prepare ? ForReading(image) : (image, 0);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(clean));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using IRandomAccessStream ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);

        uint w = decoder.PixelWidth, h = decoder.PixelHeight, max = OcrEngine.MaxImageDimension;
        double scale = forceScale ?? (lineHeight > 0 ? Math.Clamp(36 / lineHeight, 1, 3) : Math.Min(w, h) < 120 ? 3 : 1);
        scale = Math.Min(scale, Math.Min((double)max / w, (double)max / h));
        var transform = new BitmapTransform { ScaledWidth = (uint)Math.Max(1, w * scale), ScaledHeight = (uint)Math.Max(1, h * scale), InterpolationMode = BitmapInterpolationMode.Cubic };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await _screenEngine.RecognizeAsync(bitmap);
        string text = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text).Where(t => t.Length > 0));
        return Tidy(text);
    }

    /// <summary>
    /// The engine reads dark ink on light paper best. Coloured text on a dark background (code editors,
    /// video, dark mode) is turned into that: grey, inverted when the picture is mostly dark, contrast stretched.
    /// </summary>
    static (System.Windows.Media.Imaging.BitmapSource image, double lineHeight) ForReading(System.Windows.Media.Imaging.BitmapSource image)
    {
        var src = new System.Windows.Media.Imaging.FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);
        var gray = new byte[w * h];
        var histogram = new int[256];
        long sum = 0;
        for (int i = 0, j = 0; j < gray.Length; i += 4, j++)
        {
            int g = (px[i + 2] * 299 + px[i + 1] * 587 + px[i] * 114) / 1000;
            gray[j] = (byte)g;
            sum += g;
        }
        bool dark = sum / Math.Max(1, gray.Length) < 128;
        for (int j = 0; j < gray.Length; j++)
        {
            if (dark) gray[j] = (byte)(255 - gray[j]);
            histogram[gray[j]]++;
        }
        // stretch between the 2nd and 98th percentile, so faint syntax colours become solid ink
        int lo = 0, hi = 255, acc = 0, cut = gray.Length / 50;
        for (; lo < 255 && (acc += histogram[lo]) < cut; lo++) { }
        acc = 0;
        for (; hi > 0 && (acc += histogram[hi]) < cut; hi--) { }
        if (hi - lo > 32)
            for (int j = 0; j < gray.Length; j++)
                gray[j] = (byte)Math.Clamp((gray[j] - lo) * 255 / (hi - lo), 0, 255);
        var result = System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, gray, w);
        result.Freeze();
        return (result, LineHeight(gray, w, h));
    }

    /// <summary>The typical height of a line of text: the median run of rows that carry ink (0 when unclear).</summary>
    static double LineHeight(byte[] gray, int w, int h)
    {
        int need = Math.Max(1, w / 250);
        var runs = new List<int>();
        int run = 0;
        for (int y = 0; y < h; y++)
        {
            int ink = 0;
            for (int x = 0, i = y * w; x < w; x++, i++) if (gray[i] < 110 && ++ink >= need) break;
            if (ink >= need) run++;
            else { if (run >= 4) runs.Add(run); run = 0; }
        }
        if (run >= 4) runs.Add(run);
        if (runs.Count == 0) return 0;
        runs.Sort();
        return runs[runs.Count / 2];
    }

    static readonly System.Text.RegularExpressions.Regex HexO = new(@"\bO[xX]([0-9A-Fa-f]{2,})\b");

    /// <summary>Small repairs for what OCR gets wrong on screens: "Ox80070005" → "0x80070005".</summary>
    static string Tidy(string text) => HexO.Replace(text, "0x$1");

    async Task<string?> Recognise(string file)
    {
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        if (_engine == null) { _unavailable = true; return null; }
        if (!File.Exists(file)) return null;

        using var fs = File.OpenRead(file);
        using IRandomAccessStream ras = fs.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        uint max = OcrEngine.MaxImageDimension;
        var transform = new BitmapTransform();
        if (decoder.PixelWidth > max || decoder.PixelHeight > max)
        {
            double s = Math.Min((double)max / decoder.PixelWidth, (double)max / decoder.PixelHeight);
            transform.ScaledWidth = (uint)(decoder.PixelWidth * s);
            transform.ScaledHeight = (uint)(decoder.PixelHeight * s);
        }
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await _engine.RecognizeAsync(bitmap);
        var lines = result.Lines.Select(l => l.Text).Where(t => t.Length > 0);
        string joined = string.Join("\n", lines);
        return joined.Length > 20_000 ? joined[..20_000] : joined;
    }
}
