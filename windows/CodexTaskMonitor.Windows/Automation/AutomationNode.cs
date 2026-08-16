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
    int TraversalIndex);

public sealed record AutomationSnapshot(Rect WindowBounds, IReadOnlyList<AutomationNode> Nodes);

public enum SidebarMatchStatus
{
    Found,
    NotFound,
    Ambiguous
}

public sealed record SidebarMatchResult(SidebarMatchStatus Status, AutomationNode? Node);
