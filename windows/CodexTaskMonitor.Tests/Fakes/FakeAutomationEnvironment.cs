using System.Windows;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Tests.Fakes;

public sealed class FakeAutomationEnvironment : IUiAutomationSnapshotProvider, ISidebarScrollInput
{
    private readonly IReadOnlyList<AutomationSnapshot> pages;
    private readonly HashSet<SidebarInputMode> acceptedModes;
    private int index;

    private FakeAutomationEnvironment(
        IReadOnlyList<AutomationSnapshot> pages,
        int startIndex,
        IEnumerable<SidebarInputMode> acceptedModes)
    {
        this.pages = pages;
        index = startIndex;
        this.acceptedModes = acceptedModes.ToHashSet();
    }

    public List<ScrollDirection> Directions { get; } = [];
    public List<SidebarInputMode> Modes { get; } = [];
    public List<string> Actions { get; } = [];

    public static FakeAutomationEnvironment WithPages(int startIndex, params AutomationSnapshot[] pages) =>
        new(pages, startIndex, [SidebarInputMode.AutomationPattern]);

    public static FakeAutomationEnvironment WithModes(
        int startIndex,
        SidebarInputMode[] modes,
        params AutomationSnapshot[] pages) => new(pages, startIndex, modes);

    public static FakeAutomationEnvironment Ambiguous(string title)
    {
        var nodes = new List<AutomationNode>();
        for (var item = 0; item < 3; item++)
            nodes.Add(new($"filler-{item}", "ControlType.ListItem", $"filler-{item}", "", new Rect(10, 20 + item * 40, 200, 30), false, ["root", "sidebar"], item));
        nodes.Add(new("a", "ControlType.ListItem", title, "", new Rect(10, 160, 200, 30), false, ["root", "sidebar"], 4));
        nodes.Add(new("b", "ControlType.ListItem", title, "", new Rect(10, 200, 200, 30), false, ["root", "sidebar"], 5));
        return WithPages(0, new AutomationSnapshot(new Rect(0, 0, 1000, 800), nodes));
    }

    public Task<AutomationSnapshot> CaptureAsync(nint handle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(pages[index]);
    }

    public Task<bool> ScrollAsync(nint handle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directions.Add(direction);
        Modes.Add(mode);
        if (!acceptedModes.Contains(mode))
            return Task.FromResult(false);

        Actions.Add("scroll");
        index = direction == ScrollDirection.Up ? Math.Max(0, index - 1) : Math.Min(pages.Count - 1, index + 1);
        return Task.FromResult(true);
    }
}
