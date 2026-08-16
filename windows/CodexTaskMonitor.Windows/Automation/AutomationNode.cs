using System.Windows;

namespace CodexTaskMonitor.Windows.Automation;

public sealed record AutomationNode(
    string RuntimeId,
    string ControlType,
    string Name,
    string ClassName,
    Rect Bounds,
    bool IsOffscreen,
    IReadOnlyList<string> AncestorRuntimeIds,
    int TraversalIndex,
    nint NativeWindowHandle = 0);

public sealed record AutomationSnapshot(
    Rect WindowBounds,
    IReadOnlyList<AutomationNode> Nodes,
    bool IsTruncated = false);

public enum SidebarMatchStatus
{
    Found,
    NotFound,
    Ambiguous
}

public sealed record SidebarMatchResult(SidebarMatchStatus Status, AutomationNode? Node);
