using System.Windows;
using CodexTaskMonitor.Windows.Interop;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class SidebarScrollInput(
    UiAutomationSidebarScrollInput automation,
    NativeSidebarWheelInput native) : ISidebarScrollInput
{
    Task<bool> ISidebarScrollInput.ScrollAsync(nint handle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, IScrollEffectPermit permit, CancellationToken token) =>
        mode == SidebarInputMode.AutomationPattern
            ? automation.ScrollAsync(handle, region, direction, mode, permit, token)
            : native.ScrollAsync(handle, region, direction, mode, permit, token);
}
