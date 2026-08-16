using System.Windows;
using CodexTaskMonitor.Core.Sidebar;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Tests.Automation;

public sealed class SidebarMatcherTests
{
    [Fact]
    public void UniqueExactListItem_IsAccepted()
    {
        var snapshot = Snapshot(
            Node("heading", "ControlType.Text", "DemoProject", ["root", "sidebar"]),
            Node("task", "ControlType.ListItem", "Exact title", ["root", "sidebar", "project"]));

        var result = SidebarMatcher.Match(snapshot, new SidebarTarget("Exact title", SidebarThreadGroup.Project("DemoProject")));

        Assert.Equal(SidebarMatchStatus.Found, result.Status);
        Assert.Equal("task", result.Node!.RuntimeId);
    }

    [Fact]
    public void DuplicateWithoutUniqueGroupEvidence_IsAmbiguous()
    {
        var snapshot = Snapshot(
            Node("a", "ControlType.ListItem", "Same", ["root", "left"]),
            Node("b", "ControlType.ListItem", "Same", ["root", "right"]));

        var result = SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Projectless()));

        Assert.Equal(SidebarMatchStatus.Ambiguous, result.Status);
    }

    [Fact]
    public void SubstringTitle_IsNeverAccepted()
    {
        var snapshot = Snapshot(Node("task", "ControlType.ListItem", "Exact title continued", ["root", "sidebar"]));

        Assert.Equal(SidebarMatchStatus.NotFound,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Exact title", SidebarThreadGroup.Projectless())).Status);
    }

    [Fact]
    public void DuplicateTitle_UsesUniqueGroupEvidence()
    {
        var snapshot = Snapshot(
            Node("heading-a", "ControlType.Text", "Project A", ["root", "sidebar", "a"]),
            Node("task-a", "ControlType.ListItem", "Same", ["root", "sidebar", "a"]),
            Node("task-b", "ControlType.ListItem", "Same", ["root", "sidebar", "b"]));

        var result = SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Project("Project A")));

        Assert.Equal(SidebarMatchStatus.Found, result.Status);
        Assert.Equal("task-a", result.Node!.RuntimeId);
    }

    [Fact]
    public void CorrectGroupedDuplicateOffscreen_DoesNotAcceptVisibleWrongGroup()
    {
        var snapshot = Snapshot(
            Node("heading-a", "ControlType.Text", "Project A", ["root", "sidebar", "a"]),
            Node("task-a", "ControlType.ListItem", "Same", ["root", "sidebar", "a"], offscreen: true, bounds: Rect.Empty),
            Node("task-b", "ControlType.ListItem", "Same", ["root", "sidebar", "b"]));

        Assert.Equal(SidebarMatchStatus.NotFound,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Project("Project A"))).Status);
    }

    [Fact]
    public void EqualGroupScores_AreAmbiguous()
    {
        var snapshot = Snapshot(
            Node("heading", "ControlType.Text", "Project A", ["root", "sidebar"]),
            Node("task-a", "ControlType.ListItem", "Same", ["root", "sidebar", "a"]),
            Node("task-b", "ControlType.ListItem", "Same", ["root", "sidebar", "b"]));

        Assert.Equal(SidebarMatchStatus.Ambiguous,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Project("Project A"))).Status);
    }

    [Fact]
    public void DuplicateGroupHeadings_AreStructurallyAmbiguous()
    {
        var snapshot = Snapshot(
            Node("heading-a", "ControlType.Text", "Project A", ["root", "sidebar", "a"]),
            Node("heading-b", "ControlType.Text", "Project A", ["root", "sidebar", "b"]),
            Node("task-a", "ControlType.ListItem", "Same", ["root", "sidebar", "a"]),
            Node("task-b", "ControlType.ListItem", "Same", ["root", "sidebar", "b"]));

        Assert.Equal(SidebarMatchStatus.Ambiguous,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Project("Project A"))).Status);
    }

    [Fact]
    public void PinnedDuplicate_UsesTheExactPinnedGroupLabel()
    {
        var snapshot = Snapshot(
            Node("pinned-heading", "ControlType.Text", "\u7F6E\u9876", ["root", "sidebar", "pinned"]),
            Node("pinned-task", "ControlType.ListItem", "Same", ["root", "sidebar", "pinned"]),
            Node("other-task", "ControlType.ListItem", "Same", ["root", "sidebar", "other"]));

        var result = SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Pinned()));

        Assert.Equal(SidebarMatchStatus.Found, result.Status);
        Assert.Equal("pinned-task", result.Node!.RuntimeId);
    }

    [Fact]
    public void GroupHeadingAfterCandidate_IsStructurallyAmbiguous()
    {
        var snapshot = Snapshot(
            Node("task-a", "ControlType.ListItem", "Same", ["root", "sidebar", "a"]),
            Node("heading-a", "ControlType.Text", "Project A", ["root", "sidebar", "a"]),
            Node("task-b", "ControlType.ListItem", "Same", ["root", "sidebar", "b"]));

        Assert.Equal(SidebarMatchStatus.Ambiguous,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Same", SidebarThreadGroup.Project("Project A"))).Status);
    }

    [Fact]
    public void UniqueProjectItem_InWrongGroup_IsNotFound()
    {
        var snapshot = Snapshot(
            Node("heading", "ControlType.Text", "Project A", ["root", "sidebar", "other"]),
            Node("task", "ControlType.ListItem", "Exact title", ["root", "sidebar", "project"]));

        Assert.Equal(SidebarMatchStatus.NotFound,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Exact title", SidebarThreadGroup.Project("Project A"))).Status);
    }

    [Fact]
    public void UniqueSectionItem_WithoutHeading_IsAmbiguous()
    {
        var snapshot = Snapshot(Node("task", "ControlType.ListItem", "Exact title", ["root", "sidebar", "section"]));

        Assert.Equal(SidebarMatchStatus.Ambiguous,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Exact title", SidebarThreadGroup.Section("Section A"))).Status);
    }

    [Fact]
    public void UniquePinnedItem_WithHeadingAfterItem_IsAmbiguous()
    {
        var snapshot = Snapshot(
            Node("task", "ControlType.ListItem", "Exact title", ["root", "sidebar", "pinned"]),
            Node("heading", "ControlType.Text", "\u7F6E\u9876", ["root", "sidebar", "pinned"]));

        Assert.Equal(SidebarMatchStatus.Ambiguous,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Exact title", SidebarThreadGroup.Pinned())).Status);
    }

    [Fact]
    public void UniqueProjectlessItem_WithoutGroupEvidence_IsAccepted()
    {
        var snapshot = Snapshot(Node("task", "ControlType.ListItem", "Exact title", ["root", "sidebar"]));

        Assert.Equal(SidebarMatchStatus.Found,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Exact title", SidebarThreadGroup.Projectless())).Status);
    }

    [Fact]
    public void TruncatedSnapshot_NeverAcceptsAnOtherwiseUniqueItem()
    {
        var snapshot = new AutomationSnapshot(
            new Rect(0, 0, 1000, 800),
            [Node("task", "ControlType.ListItem", "Exact title", ["root", "sidebar"])],
            IsTruncated: true);

        Assert.Equal(SidebarMatchStatus.Ambiguous,
            SidebarMatcher.Match(snapshot, new SidebarTarget("Exact title", SidebarThreadGroup.Projectless())).Status);
    }

    private static AutomationNode Node(
        string id,
        string controlType,
        string name,
        string[] ancestors,
        bool offscreen = false,
        Rect? bounds = null) =>
        new(id, controlType, name, "", bounds ?? new Rect(20, 20, 220, 40), offscreen, ancestors, 0);

    private static AutomationSnapshot Snapshot(params AutomationNode[] nodes) =>
        new(new Rect(0, 0, 1000, 800), nodes.Select((node, index) => node with { TraversalIndex = index }).ToArray());
}
