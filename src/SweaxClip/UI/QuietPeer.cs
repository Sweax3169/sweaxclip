using System.Windows;
using System.Windows.Automation.Peers;

namespace SweaxClip.UI;

/// <summary>
/// UI Automation view of a pop-up window that shows only the window itself. Whenever any program on the PC listens to
/// UI Automation (the touch keyboard and emoji panel, PowerToys, screen tools), WPF walks every element of a window and
/// reports its changes after each layout pass. The panels lay out on every animation frame, so that walk cost tens to
/// hundreds of milliseconds per frame and made opening and browsing stutter.
/// </summary>
public sealed class QuietPeer(FrameworkElement owner, string name) : FrameworkElementAutomationPeer(owner)
{
    protected override List<AutomationPeer>? GetChildrenCore() => null;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Window;
    protected override string GetClassNameCore() => "SweaxClip";
    protected override string GetNameCore() => name;
}
