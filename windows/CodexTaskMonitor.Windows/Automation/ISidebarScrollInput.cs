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

public sealed record SidebarScrollRegion(
    Rect Bounds,
    Point InputPoint,
    string ContainerRuntimeId,
    string InputNodeRuntimeId,
    nint ExpectedHitTestWindow);

public interface ISidebarScrollInput
{
    Task<bool> ScrollAsync(nint windowHandle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token);
}
