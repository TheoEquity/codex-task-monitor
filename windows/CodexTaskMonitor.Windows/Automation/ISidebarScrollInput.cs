using System.Windows;

namespace CodexTaskMonitor.Windows.Automation;

public enum ScrollDirection
{
    Up,
    Down
}

public enum SidebarInputMode
{
    AutomationPattern,
    PostedMessage,
    PhysicalFallback
}

public enum SidebarScrollStatus
{
    Found,
    NotFound,
    Ambiguous,
    RegionUnavailable,
    TimedOut
}

public sealed record SidebarScrollResult(SidebarScrollStatus Status, AutomationNode? Node);

public interface ISidebarScrollInput
{
    Task<bool> ScrollAsync(nint windowHandle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token);
}
