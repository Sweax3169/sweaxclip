using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// The panel never takes focus, so it gets its keys from the keyboard hook rather than from WPF. This turns them into
/// what WPF would have given it: the key with its modifiers, the character typed in the user's keyboard layout, and
/// the editing a text box does by itself (Backspace, Delete, the caret keys, Ctrl+A, Ctrl+V).
/// </summary>
internal static class PanelKeys
{
    /// <summary>The key and modifiers; Ctrl+Alt is AltGr (Turkish @, €, ₺…), which types rather than commands.</summary>
    public static (Key key, ModifierKeys mods) Read(CapturedKey k)
    {
        var mods = ModifierKeys.None;
        bool altGr = k.Ctrl && k.Alt;
        if (k.Ctrl && !altGr) mods |= ModifierKeys.Control;
        if (k.Alt && !altGr) mods |= ModifierKeys.Alt;
        if (k.Shift) mods |= ModifierKeys.Shift;
        return (KeyInterop.KeyFromVirtualKey(k.Vk), mods);
    }

    /// <summary>The character(s) the key types in the layout of <paramref name="window"/>, or null for keys that don't type.</summary>
    public static string? Text(CapturedKey k, IntPtr window)
    {
        if (k.Ctrl && !k.Alt) return null;
        var state = new byte[256];
        if (k.Shift) state[Win32.VK_SHIFT] = state[Win32.VK_LSHIFT] = 0x80;
        if (k.Ctrl) state[Win32.VK_CONTROL] = state[Win32.VK_LCONTROL] = 0x80;
        if (k.Alt) state[Win32.VK_MENU] = state[Win32.VK_RMENU] = 0x80;
        if (Win32.CapsLockOn(window)) state[Win32.VK_CAPITAL] = 0x01;
        uint thread = window == IntPtr.Zero ? 0 : Win32.GetWindowThreadProcessId(window, out _);
        var buf = new StringBuilder(8);
        // flag 4: leave the keyboard's dead-key state alone, so the app under the panel is not affected
        int n = Win32.ToUnicodeEx((uint)k.Vk, (uint)k.Scan, state, buf, buf.Capacity, 4, Win32.GetKeyboardLayout(thread));
        if (n <= 0) return null;
        string s = buf.ToString(0, Math.Min(n, buf.Length));
        return s.Any(char.IsControl) ? null : s;
    }

    /// <summary>What a focused text box would do with the key. Returns false when it isn't an editing key.</summary>
    public static bool Edit(TextBox box, Key key, ModifierKeys mods, string? text)
    {
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        int caret = box.CaretIndex, len = box.Text.Length;
        switch (key)
        {
            case Key.Back:
                if (box.SelectionLength > 0) box.SelectedText = "";
                else if (caret > 0)
                {
                    int from = ctrl ? WordStart(box.Text, caret) : caret - 1;
                    box.Text = box.Text.Remove(from, caret - from);
                    box.CaretIndex = from;
                }
                return true;
            case Key.Delete when len > 0:
                if (box.SelectionLength > 0) box.SelectedText = "";
                else if (caret < len) { box.Text = box.Text.Remove(caret, 1); box.CaretIndex = caret; }
                return true;
            case Key.Left when len > 0:
                Move(box, ctrl ? WordStart(box.Text, caret) : Math.Max(0, caret - 1), shift);
                return true;
            case Key.Right when len > 0:
                Move(box, ctrl ? WordEnd(box.Text, caret) : Math.Min(len, caret + 1), shift);
                return true;
            case Key.Home when len > 0: Move(box, 0, shift); return true;
            case Key.End when len > 0: Move(box, len, shift); return true;
            case Key.A when ctrl: box.SelectAll(); return true;
            case Key.V when ctrl:
                try { if (Clipboard.ContainsText()) Type(box, Clipboard.GetText().Replace("\r", " ").Replace("\n", " ").Trim()); } catch { }
                return true;
        }
        if (text == null) return false;
        Type(box, text);
        return true;
    }

    static void Type(TextBox box, string text)
    {
        int at = box.SelectionStart;
        box.SelectedText = text;
        box.Select(at + text.Length, 0);
    }

    /// <summary>Moves the caret; with Shift the selection grows from where it started.</summary>
    static void Move(TextBox box, int to, bool extend)
    {
        if (!extend) { box.Select(to, 0); return; }
        int anchor = box.CaretIndex == box.SelectionStart + box.SelectionLength && box.SelectionLength > 0 ? box.SelectionStart
                   : box.SelectionLength > 0 ? box.SelectionStart + box.SelectionLength : box.CaretIndex;
        box.Select(Math.Min(anchor, to), Math.Abs(to - anchor));
    }

    static int WordStart(string s, int i)
    {
        while (i > 0 && char.IsWhiteSpace(s[i - 1])) i--;
        while (i > 0 && !char.IsWhiteSpace(s[i - 1])) i--;
        return i;
    }

    static int WordEnd(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        return i;
    }
}

/// <summary>A blinking caret for a text box in a window that never takes focus: WPF draws its own caret only with focus.</summary>
internal sealed class Caret : Adorner
{
    readonly TextBox _box;
    readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    bool _on = true;

    Caret(TextBox box) : base(box)
    {
        _box = box;
        IsHitTestVisible = false;
        _blink.Tick += (_, _) => { _on = !_on; InvalidateVisual(); };
        box.SelectionChanged += (_, _) => Restart();
        box.TextChanged += (_, _) => Restart();
        box.IsVisibleChanged += (_, _) => Restart();
    }

    public static void Attach(TextBox box) => box.Loaded += (_, _) =>
    {
        if (AdornerLayer.GetAdornerLayer(box) is { } layer && layer.GetAdorners(box)?.OfType<Caret>().Any() != true) layer.Add(new Caret(box));
    };

    void Restart()
    {
        _on = true;
        _blink.Stop();
        if (_box.IsVisible) _blink.Start();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_on || _box.IsKeyboardFocused || _box.SelectionLength > 0) return;
        var r = _box.GetRectFromCharacterIndex(_box.CaretIndex);
        if (r.IsEmpty || double.IsInfinity(r.X) || double.IsNaN(r.X)) return;
        dc.DrawRectangle(_box.CaretBrush ?? _box.Foreground, null, new Rect(Math.Round(r.X), r.Y, 1.5, r.Height));
    }
}
