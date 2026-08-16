using System.Windows;
using CodexTaskMonitor.Windows.Interop;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class SidebarScrollInput(
    UiAutomationSidebarScrollInput automation,
    NativeSidebarWheelInput native) : ISidebarScrollInput
{
    public Task<bool> ScrollAsync(nint handle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token) =>
        mode == SidebarInputMode.AutomationPattern
            ? automation.ScrollAsync(handle, region, direction, mode, token)
            : native.ScrollAsync(handle, region, direction, mode, token);
}
