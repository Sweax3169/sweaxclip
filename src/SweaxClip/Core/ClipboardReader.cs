using System.Runtime.InteropServices;
using System.Text;
using SweaxClip.Native;

namespace SweaxClip.Core;

public sealed class CapturedClip
{
    public string? Text, Html, Rtf;
    public byte[]? Png, Dib;
    public string[]? Files;
    public string? AppPath, AppName, Title;
    public long When = TimeText.Now;
    public bool LikelyScreenshot;
    public bool HasImage => Png != null || Dib != null;
}

/// <summary>Reads the clipboard with raw Win32 calls: fast, no COM, and it can see the privacy flags.</summary>
public static class ClipboardReader
{
    public const string MarkerFormat = "SweaxClip.Internal";

    static readonly uint FmtHtml = Win32.RegisterClipboardFormat("HTML Format");
    static readonly uint FmtRtf = Win32.RegisterClipboardFormat("Rich Text Format");
    static readonly uint FmtPng = Win32.RegisterClipboardFormat("PNG");
    static readonly uint FmtPngMime = Win32.RegisterClipboardFormat("image/png");
    static readonly uint FmtExclude = Win32.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    static readonly uint FmtViewerIgnore = Win32.RegisterClipboardFormat("Clipboard Viewer Ignore");
    static readonly uint FmtCanInclude = Win32.RegisterClipboardFormat("CanIncludeInClipboardHistory");
    public static readonly uint FmtMarker = Win32.RegisterClipboardFormat(MarkerFormat);

    const int MaxText = 32 * 1024 * 1024;
    const int MaxImage = 256 * 1024 * 1024;

    public enum Result { Ok, Busy, Empty, Private, Internal }

    public static Result Read(IntPtr owner, bool respectPrivate, bool wantText, bool wantImages, bool wantFiles, out CapturedClip clip)
    {
        clip = new CapturedClip();
        bool opened = false;
        for (int i = 0; i < 12 && !opened; i++)
        {
            opened = Win32.OpenClipboard(owner);
            if (!opened) Thread.Sleep(10 + i * 5);
        }
        if (!opened) return Result.Busy;
        try
        {
            var formats = new HashSet<uint>();
            for (uint f = Win32.EnumClipboardFormats(0); f != 0; f = Win32.EnumClipboardFormats(f)) formats.Add(f);
            if (formats.Count == 0) return Result.Empty;
            if (formats.Contains(FmtMarker)) return Result.Internal;

            if (respectPrivate)
            {
                if (formats.Contains(FmtExclude) || formats.Contains(FmtViewerIgnore)) return Result.Private;
                if (formats.Contains(FmtCanInclude))
                {
                    var b = ReadBytes(FmtCanInclude, 16);
                    if (b is { Length: >= 4 } && BitConverter.ToInt32(b, 0) == 0) return Result.Private;
                }
            }

            if (wantFiles && formats.Contains(Win32.CF_HDROP))
            {
                clip.Files = ReadFiles();
                if (clip.Files.Length > 0) return Result.Ok;
            }

            if (wantText && formats.Contains(Win32.CF_UNICODETEXT)) clip.Text = ReadUnicode();
            bool hasImage = formats.Contains(FmtPng) || formats.Contains(FmtPngMime) || formats.Contains(Win32.CF_DIB) || formats.Contains(Win32.CF_DIBV5) || formats.Contains(Win32.CF_BITMAP);

            bool textIsMeaningful = !string.IsNullOrWhiteSpace(clip.Text);
            // Browsers put a URL or nothing next to a copied image; the image is what the user meant.
            bool preferImage = hasImage && (!textIsMeaningful || (clip.Text!.Trim().IndexOfAny([' ', '\n']) < 0 && clip.Text.Length < 2048 && !formats.Contains(FmtRtf)));

            if (wantImages && preferImage)
            {
                if (formats.Contains(FmtPng)) clip.Png = ReadBytes(FmtPng, MaxImage);
                else if (formats.Contains(FmtPngMime)) clip.Png = ReadBytes(FmtPngMime, MaxImage);
                if (clip.Png == null || !ImageTools.IsPng(clip.Png))
                {
                    clip.Png = null;
                    clip.Dib = formats.Contains(Win32.CF_DIB) || formats.Contains(Win32.CF_BITMAP) ? ReadBytes(Win32.CF_DIB, MaxImage) : null;
                    clip.Dib ??= ReadBytes(Win32.CF_DIBV5, MaxImage);
                }
                if (clip.HasImage) { clip.Text = null; return Result.Ok; }
            }

            if (wantText && textIsMeaningful)
            {
                if (formats.Contains(FmtHtml)) clip.Html = ReadUtf8(FmtHtml, 8 * 1024 * 1024);
                if (formats.Contains(FmtRtf)) clip.Rtf = ReadUtf8(FmtRtf, 8 * 1024 * 1024, ascii: true);
                return Result.Ok;
            }
            return Result.Empty;
        }
        catch (Exception ex)
        {
            Paths.Log("clipboard read failed: " + ex.Message);
            return Result.Empty;
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    static string? ReadUnicode()
    {
        IntPtr h = Win32.GetClipboardData(Win32.CF_UNICODETEXT);
        if (h == IntPtr.Zero) return null;
        IntPtr p = Win32.GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try
        {
            long size = (long)Win32.GlobalSize(h);
            int maxChars = (int)Math.Min(size / 2, MaxText);
            string s = Marshal.PtrToStringUni(p, maxChars);
            int z = s.IndexOf('\0');
            return z >= 0 ? s[..z] : s;
        }
        finally { Win32.GlobalUnlock(h); }
    }

    static byte[]? ReadBytes(uint format, int max)
    {
        IntPtr h = Win32.GetClipboardData(format);
        if (h == IntPtr.Zero) return null;
        IntPtr p = Win32.GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try
        {
            long size = (long)Win32.GlobalSize(h);
            if (size <= 0 || size > max) return null;
            var buf = new byte[size];
            Marshal.Copy(p, buf, 0, (int)size);
            return buf;
        }
        finally { Win32.GlobalUnlock(h); }
    }

    static string? ReadUtf8(uint format, int max, bool ascii = false)
    {
        var b = ReadBytes(format, max);
        if (b == null) return null;
        int len = Array.IndexOf(b, (byte)0);
        if (len < 0) len = b.Length;
        return ascii ? Encoding.Latin1.GetString(b, 0, len) : Encoding.UTF8.GetString(b, 0, len);
    }

    static string[] ReadFiles()
    {
        IntPtr h = Win32.GetClipboardData(Win32.CF_HDROP);
        if (h == IntPtr.Zero) return [];
        uint n = Win32.DragQueryFile(h, 0xFFFFFFFF, null, 0);
        var list = new List<string>((int)Math.Min(n, 10_000));
        for (uint i = 0; i < n && i < 10_000; i++)
        {
            int len = (int)Win32.DragQueryFile(h, i, null, 0);
            var sb = new StringBuilder(len + 1);
            Win32.DragQueryFile(h, i, sb, sb.Capacity);
            list.Add(sb.ToString());
        }
        return [.. list];
    }
}
