using SweaxClip.Core;

namespace SweaxClip.UI;

/// <summary>What the app needs from a Win+V panel, whichever style is chosen in Settings.</summary>
public interface IQuickPanel
{
    ClipListModel Model { get; }
    bool IsVisible { get; }
    void Toggle(IntPtr previous);
    void HideNow();
    void Close();
}
