using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using SweaxClip.Native;

namespace SweaxClip.Core;

/// <summary>Puts an archived clip back on the clipboard and pastes it into the window the user came from.</summary>
public static class Paster
{
    public static bool SetClipboard(ClipItem item, bool plain)
    {
        var data = new DataObject();
        switch (item.Kind)
        {
            case ClipKind.Image or ClipKind.Screenshot:
                var bmp = ImageTools.Load(item.Blob, 0);
                if (bmp == null) return false;
                data.SetImage(bmp);
                try
                {
                    var png = File.ReadAllBytes(Paths.Abs(item.Blob!));
                    data.SetData("PNG", new MemoryStream(png), false);
                }
                catch { }
                break;
            case ClipKind.Files:
                var files = new StringCollection();
                files.AddRange(item.Files);
                if (plain) data.SetText(item.FullText, TextDataFormat.UnicodeText);
                else
                {
                    data.SetFileDropList(files);
                    data.SetData("Preferred DropEffect", new MemoryStream([1, 0, 0, 0]), false);
                    data.SetText(item.FullText, TextDataFormat.UnicodeText);
                }
                break;
            default:
                string text = item.Text ?? "";
                data.SetText(text, TextDataFormat.UnicodeText);
                if (!plain)
                {
                    if (!string.IsNullOrEmpty(item.Html)) data.SetData(DataFormats.Html, item.Html);
                    if (!string.IsNullOrEmpty(item.Rtf)) data.SetData(DataFormats.Rtf, item.Rtf);
                }
                break;
        }
        data.SetData(ClipboardReader.MarkerFormat, item.Id.ToString());

        for (int i = 0; i < 8; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (COMException) { Thread.Sleep(25); }
            catch (ExternalException) { Thread.Sleep(25); }
        }
        return false;
    }

    /// <summary>Returns focus to <paramref name="target"/> and sends Ctrl+V once modifiers are released.</summary>
    /// <summary>Reopens Windows Search (Win+S) and pastes into its box — for clips picked while Start or Search was open.</summary>
    public static async Task PasteIntoSearch()
    {
        Win32.Send(Win32.Key(Win32.VK_LWIN, false), Win32.Key(Win32.VK_S, false), Win32.Key(Win32.VK_S, true), Win32.Key(Win32.VK_LWIN, true));
        await Task.Delay(420);
        await PasteInto(IntPtr.Zero);
    }

    public static async Task PasteInto(IntPtr target)
    {
        if (target != IntPtr.Zero && Win32.IsWindow(target)) Win32.ForceForeground(target);
        await Task.Delay(45);

        var inputs = new List<Win32.INPUT>();
        foreach (int vk in new[] { Win32.VK_LWIN, Win32.VK_RWIN, Win32.VK_LSHIFT, Win32.VK_RSHIFT, Win32.VK_LMENU, Win32.VK_RMENU })
            if (Win32.IsKeyDown(vk)) inputs.Add(Win32.Key(vk, true));
        if (inputs.Count > 0) inputs.Add(Win32.Key(Win32.VK_MASK, false));
        if (inputs.Count > 0) inputs.Add(Win32.Key(Win32.VK_MASK, true));
        bool ctrlHeld = Win32.IsKeyDown(Win32.VK_CONTROL);
        if (!ctrlHeld) inputs.Add(Win32.Key(Win32.VK_LCONTROL, false));
        inputs.Add(Win32.Key(Win32.VK_V, false));
        inputs.Add(Win32.Key(Win32.VK_V, true));
        if (!ctrlHeld) inputs.Add(Win32.Key(Win32.VK_LCONTROL, true));
        Win32.Send([.. inputs]);
    }
}
