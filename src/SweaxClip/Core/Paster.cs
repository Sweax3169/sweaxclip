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

    /// <summary>Reopens Windows Search (Win+S) and pastes into its box — for clips picked while Start or Search was open.</summary>
    public static async Task PasteIntoSearch()
    {
        Win32.Send(Win32.Key(Win32.VK_LWIN, false), Win32.Key(Win32.VK_S, false), Win32.Key(Win32.VK_S, true), Win32.Key(Win32.VK_LWIN, true));
        await Task.Delay(420);
        await PasteInto(IntPtr.Zero);
    }

    /// <summary>
    /// Pastes into <paramref name="target"/>. The panel never takes focus, so the app is normally still in front with its
    /// text cursor where it was, and Ctrl+V goes at once. If another window came to the front meanwhile, focus goes back
    /// to <paramref name="target"/> (and to <paramref name="focus"/>, the text box inside it) first.
    /// </summary>
    public static async Task PasteInto(IntPtr target, IntPtr focus = default)
    {
        if (target != IntPtr.Zero && Win32.IsWindow(target))
        {
            if (Win32.GetForegroundWindow() != target)
            {
                Win32.ForceForeground(target);
                await WaitForFocus(target, focus);
            }
        }
        else await Task.Delay(45);

        // let go of any Win, Shift or Alt still held; the unused key goes first, so a released Alt or Win
        // can't open the menu bar or Start, which would take the Ctrl+V
        var inputs = new List<Win32.INPUT>();
        var held = new[] { Win32.VK_LWIN, Win32.VK_RWIN, Win32.VK_LSHIFT, Win32.VK_RSHIFT, Win32.VK_LMENU, Win32.VK_RMENU }.Where(Win32.IsKeyDown).ToList();
        if (held.Count > 0)
        {
            inputs.Add(Win32.Key(Win32.VK_MASK, false));
            inputs.Add(Win32.Key(Win32.VK_MASK, true));
            foreach (int vk in held) inputs.Add(Win32.Key(vk, true));
        }
        bool ctrlHeld = Win32.IsKeyDown(Win32.VK_CONTROL);
        if (!ctrlHeld) inputs.Add(Win32.Key(Win32.VK_LCONTROL, false));
        inputs.Add(Win32.Key(Win32.VK_V, false));
        inputs.Add(Win32.Key(Win32.VK_V, true));
        if (!ctrlHeld) inputs.Add(Win32.Key(Win32.VK_LCONTROL, true));
        Win32.Send([.. inputs]);
    }

    /// <summary>
    /// The panel took activation, so the target app has to hand focus back to its text box when it is reactivated.
    /// Browsers keep focus on the top-level window and are ready at once; Notepad, Word, WordPad and most Win32 and
    /// XAML apps move it to a child control only after they handle the activation, and a Ctrl+V that arrives
    /// earlier goes to the frame and is lost. Wait for that, and put focus back ourselves if the app does not.
    /// </summary>
    static async Task WaitForFocus(IntPtr target, IntPtr focus)
    {
        bool child = focus != IntPtr.Zero && focus != target && Win32.IsWindow(focus) && Win32.IsWindowVisible(focus);
        bool nudged = false;
        for (int waited = 0; waited < 600; waited += 15)
        {
            if (Win32.GetForegroundWindow() == target)
            {
                IntPtr now = Win32.FocusOf(target);
                // without a remembered text box (UWP frames report none), give the app a moment and go
                if (child ? now == focus : now != IntPtr.Zero || waited >= 150)
                {
                    await Task.Delay(30);
                    return;
                }
                if (child && !nudged && waited >= 120)
                {
                    Win32.GiveFocus(focus);
                    nudged = true;
                }
            }
            await Task.Delay(15);
        }
    }
}
