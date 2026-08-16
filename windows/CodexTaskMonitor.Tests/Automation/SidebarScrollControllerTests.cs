using System.Windows;
using CodexTaskMonitor.Core.Sidebar;
using CodexTaskMonitor.Tests.Fakes;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Tests.Automation;

public sealed class SidebarScrollControllerTests
{
    [Fact]
    public async Task Reveal_ResetsUpThenScansDownUntilUniqueTargetIsVisible()
    {
        var environment = FakeAutomationEnvironment.WithPages(1,
            Page("top"), Page("middle"), Page("target", targetTitle: "Wanted"));
        var controller = new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, maxSteps: 80, timeout: TimeSpan.FromSeconds(8));

        var result = await controller.RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.Found, result.Status);
        Assert.Contains(ScrollDirection.Up, environment.Directions);
        Assert.Equal(ScrollDirection.Down, environment.Directions[^1]);
        Assert.Equal(SidebarInputMode.AutomationPattern, environment.Modes[0]);
        Assert.DoesNotContain(environment.Actions, action => action == "click");
    }

    [Fact]
    public async Task Reveal_StopsOnAmbiguousMatchWithoutScrolling()
    {
        var environment = FakeAutomationEnvironment.Ambiguous("Same");
        var controller = new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8));

        var result = await controller.RevealAsync(123, new SidebarTarget("Same", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.Ambiguous, result.Status);
        Assert.Empty(environment.Directions);
    }

    [Fact]
    public async Task Reveal_MissingSidebarRegionFailsWithoutInput()
    {
        var environment = FakeAutomationEnvironment.WithPages(0,
            new AutomationSnapshot(new Rect(0, 0, 1000, 800), []));

        var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8))
            .RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.RegionUnavailable, result.Status);
        Assert.Empty(environment.Actions);
    }

    [Fact]
    public async Task Reveal_UsesPhysicalFallbackOnlyAfterEarlierModesRejectInput()
    {
        var environment = FakeAutomationEnvironment.WithModes(1, [SidebarInputMode.PhysicalFallback],
            Page("top"), Page("middle"), Page("target", targetTitle: "Wanted"));

        var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8))
            .RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.Found, result.Status);
        Assert.Equal(
            [SidebarInputMode.AutomationPattern, SidebarInputMode.PostedMessage, SidebarInputMode.PhysicalFallback],
            environment.Modes.Distinct());
    }

    [Fact]
    public async Task Reveal_StopsAtEightyScrollAttempts()
    {
        var pages = Enumerable.Range(0, 100).Select(index => Page($"page-{index}")).ToArray();
        var environment = FakeAutomationEnvironment.WithPages(99, pages);

        var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromMinutes(1))
            .RevealAsync(123, new SidebarTarget("missing", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.NotFound, result.Status);
        Assert.Equal(80, environment.Directions.Count);
    }

    [Fact]
    public async Task Reveal_ZeroTimeoutIsDeterministicallyTimedOut()
    {
        var environment = FakeAutomationEnvironment.WithPages(0, Page("top"));

        var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.Zero)
            .RevealAsync(123, new SidebarTarget("missing", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task Reveal_HonorsCancellationBeforeInput()
    {
        var environment = FakeAutomationEnvironment.WithPages(0, Page("top"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8))
                .RevealAsync(123, new SidebarTarget("missing", SidebarThreadGroup.Projectless()), cancellation.Token));

        Assert.Empty(environment.Actions);
    }

    [Fact]
    public async Task Reveal_OffscreenTargetBecomesVisibleBeforeSuccess()
    {
        var environment = FakeAutomationEnvironment.WithPages(0,
            Page("top", "Wanted", targetOffscreen: true), Page("visible", "Wanted"));

        var result = await new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8))
            .RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.Found, result.Status);
        Assert.False(result.Node!.IsOffscreen);
    }

    private static AutomationSnapshot Page(string key, string? targetTitle = null, bool targetOffscreen = false)
    {
        var nodes = new List<AutomationNode>();
        for (var item = 0; item < 4; item++)
            nodes.Add(new($"{key}-{item}", "ControlType.ListItem", $"{key}-filler-{item}", "",
                new Rect(10, 20 + item * 40, 200, 30), false, ["root", "sidebar"], item));
        if (targetTitle is not null)
            nodes.Add(new($"{key}-target", "ControlType.ListItem", targetTitle, "",
                targetOffscreen ? Rect.Empty : new Rect(10, 160, 200, 30), targetOffscreen,
                ["root", "sidebar"], 4));
        return new AutomationSnapshot(new Rect(0, 0, 1000, 800), nodes);
    }
}
