using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SweaxClip.Native;

namespace SweaxClip.Core;

/// <summary>Frozen, cached shell icons for source apps and files.</summary>
public static class IconCache
{
    static readonly ConcurrentDictionary<string, ImageSource?> Exe = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, ImageSource?> Ext = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForExe(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return Exe.GetOrAdd(path, p =>
        {
            try
            {
                var large = new IntPtr[1];
                if (Win32.ExtractIconEx(p, 0, large, null, 1) == 0 || large[0] == IntPtr.Zero) return null;
                return FromHIcon(large[0]);
            }
            catch { return null; }
        });
    }

    public static ImageSource? ForFile(string path)
    {
        string key = Directory.Exists(path) ? "<dir>" : Path.GetExtension(path);
        if (key is ".exe" or ".lnk" or ".ico") key = path;
        return Ext.GetOrAdd(key, _ =>
        {
            try
            {
                var info = new Win32.SHFILEINFO();
                uint attrs = Directory.Exists(path) ? 0x10u : 0x80u;
                uint flags = Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON;
                if (!File.Exists(path) && !Directory.Exists(path)) flags |= Win32.SHGFI_USEFILEATTRIBUTES;
                Win32.SHGetFileInfo(path, attrs, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(info), flags);
                return info.hIcon == IntPtr.Zero ? null : FromHIcon(info.hIcon);
            }
            catch { return null; }
        });
    }

    static ImageSource FromHIcon(IntPtr h)
    {
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { Win32.DestroyIcon(h); }
    }
}
