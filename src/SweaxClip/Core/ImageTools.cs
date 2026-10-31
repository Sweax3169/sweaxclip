using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SweaxClip.Core;

public static class ImageTools
{
    public const int ThumbWidth = 640;

    /// <summary>Decodes clipboard image data (PNG stream preferred, DIB as fallback) into a frozen bitmap.</summary>
    public static BitmapSource? Decode(byte[]? png, byte[]? dib)
    {
        if (png is { Length: > 8 })
        {
            try { return DecodeStream(new MemoryStream(png)); } catch { }
        }
        if (dib is { Length: > 40 })
        {
            try { return DecodeStream(new MemoryStream(DibToBmp(dib))); } catch { }
        }
        return null;
    }

    static BitmapSource DecodeStream(Stream s)
    {
        var dec = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource frame = dec.Frames[0];
        if (frame.Format != PixelFormats.Bgra32 && frame.Format != PixelFormats.Bgr32)
            frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        if (Math.Abs(frame.DpiX - 96) > 0.5 || Math.Abs(frame.DpiY - 96) > 0.5)
        {
            // Normalise DPI so images display at their pixel size.
            int stride = frame.PixelWidth * 4;
            var px = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(px, stride, 0);
            frame = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, frame.Format, null, px, stride);
        }
        frame.Freeze();
        return frame;
    }

    /// <summary>Prefixes a BITMAPFILEHEADER so WIC can read a packed DIB.</summary>
    static byte[] DibToBmp(byte[] dib)
    {
        int headerSize = BitConverter.ToInt32(dib, 0);
        ushort bitCount = BitConverter.ToUInt16(dib, 14);
        uint compression = BitConverter.ToUInt32(dib, 16);
        uint clrUsed = BitConverter.ToUInt32(dib, 32);
        uint colors = clrUsed != 0 ? clrUsed : bitCount <= 8 ? 1u << bitCount : 0;
        int masks = headerSize == 40 && compression == 3 ? 12 : headerSize == 40 && compression == 6 ? 16 : 0;
        int offBits = 14 + headerSize + masks + (int)colors * 4;
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(offBits).CopyTo(bmp, 10);
        Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
        return bmp;
    }

    public static string PixelHash(BitmapSource src)
    {
        BitmapSource b = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int stride = b.PixelWidth * 4;
        var px = new byte[stride * b.PixelHeight];
        b.CopyPixels(px, stride, 0);
        if (src.Format == PixelFormats.Bgr32) for (int i = 3; i < px.Length; i += 4) px[i] = 255; // ignore padding byte
        using var sha = SHA256.Create();
        sha.TransformBlock(System.Text.Encoding.ASCII.GetBytes($"I:{b.PixelWidth}x{b.PixelHeight}:"), 0, 0, null, 0);
        sha.TransformFinalBlock(px, 0, px.Length);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    public static string TextHash(string prefix, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(prefix + text);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>Writes the original image; returns the archive-relative path and the size on disk.</summary>
    public static (string rel, long size) SaveOriginal(BitmapSource src, byte[]? originalPng, string hash)
    {
        string rel = Path.Combine("blobs", hash[..2], hash + ".png");
        string abs = Paths.Abs(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        if (!File.Exists(abs))
        {
            string tmp = abs + ".tmp";
            if (originalPng is { Length: > 8 } && IsPng(originalPng))
                File.WriteAllBytes(tmp, originalPng);
            else
            {
                var enc = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
                enc.Frames.Add(BitmapFrame.Create(src));
                using var fs = File.Create(tmp);
                enc.Save(fs);
            }
            File.Move(tmp, abs, true);
        }
        return (rel.Replace('\\', '/'), new FileInfo(abs).Length);
    }

    public static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    /// <summary>Creates a small preview (JPEG when opaque, PNG when it has transparency).</summary>
    public static string? SaveThumb(string sourceFile, string hash)
    {
        try
        {
            var bi = new BitmapImage();
            using (var fs = File.OpenRead(sourceFile))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.StreamSource = fs;
                bi.EndInit();
            }
            BitmapSource src = bi;
            if (bi.PixelWidth > ThumbWidth)
            {
                // Re-decode with the WIC (Fant) scaler for a clean downsample.
                var scaled = new BitmapImage();
                using var fs2 = File.OpenRead(sourceFile);
                scaled.BeginInit();
                scaled.CacheOption = BitmapCacheOption.OnLoad;
                scaled.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                scaled.DecodePixelWidth = ThumbWidth;
                scaled.StreamSource = fs2;
                scaled.EndInit();
                src = scaled;
            }
            bool alpha = HasTransparency(src);
            string rel = Path.Combine("thumbs", hash[..2], hash + (alpha ? ".png" : ".jpg"));
            string abs = Paths.Abs(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            BitmapEncoder enc = alpha ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 86 };
            enc.Frames.Add(BitmapFrame.Create(src));
            using (var o = File.Create(abs)) enc.Save(o);
            return rel.Replace('\\', '/');
        }
        catch (Exception ex)
        {
            Paths.Log("thumb failed: " + ex.Message);
            return null;
        }
    }

    static bool HasTransparency(BitmapSource s)
    {
        if (s.Format != PixelFormats.Bgra32 && s.Format != PixelFormats.Pbgra32 && !s.Format.ToString().Contains("A", StringComparison.Ordinal)) return false;
        var b = s.Format == PixelFormats.Bgra32 ? s : new FormatConvertedBitmap(s, PixelFormats.Bgra32, null, 0);
        int stride = b.PixelWidth * 4;
        var px = new byte[stride * b.PixelHeight];
        b.CopyPixels(px, stride, 0);
        for (int i = 3; i < px.Length; i += 4) if (px[i] < 250) return true;
        return false;
    }

    /// <summary>Loads an archive image for display, decoded no larger than needed.</summary>
    public static BitmapSource? Load(string? rel, int decodeWidth)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        string abs = Path.IsPathRooted(rel) ? rel : Paths.Abs(rel);
        if (!File.Exists(abs)) return null;
        try
        {
            var bi = new BitmapImage();
            using var fs = new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
            bi.StreamSource = fs;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    public static (int w, int h) ProbeSize(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            return (dec.Frames[0].PixelWidth, dec.Frames[0].PixelHeight);
        }
        catch { return (0, 0); }
    }

    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic", ".jfif" };
}
