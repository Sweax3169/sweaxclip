using SweaxClip.Core;

namespace SweaxClip.UI;

/// <summary>What the app needs from a Win+V panel, whichever style is chosen in Settings.</summary>
public interface IQuickPanel
{
    ClipListModel Model { get; }
    bool IsVisible { get; }
    /// <summary>The window the panel opened over; it keeps focus while the panel is open.</summary>
    IntPtr Target { get; }
    void Toggle(IntPtr previous);
    void HideNow();
    void Close();
    /// <summary>A key from the keyboard hook: the panel never takes focus, so this is how it hears the keyboard.</summary>
    void OnKey(CapturedKey key);
    /// <summary>Builds the rows once off-screen, so the first Win+V is as quick as the rest.</summary>
    Task Warm();
}
