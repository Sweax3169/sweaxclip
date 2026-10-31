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
