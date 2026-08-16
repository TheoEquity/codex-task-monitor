using System.Windows;
using System.Diagnostics;
using CodexTaskMonitor.Core.Sidebar;
using CodexTaskMonitor.Tests.Fakes;
using CodexTaskMonitor.Windows.Automation;
using CodexTaskMonitor.Windows.Interop;

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

    [Fact]
    public async Task Reveal_TimesOutAndCancelsHungSnapshot()
    {
        var snapshots = new HangingSnapshotProvider();
        var controller = new SidebarScrollController(snapshots, new NeverCalledScrollInput(), TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromMilliseconds(50));

        var result = await controller.RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.TimedOut, result.Status);
        Assert.True(snapshots.CancellationObserved);
    }

    [Fact]
    public async Task Reveal_TimesOutAndCancelsHungInput()
    {
        var environment = FakeAutomationEnvironment.WithPages(0, Page("top"));
        var input = new HangingScrollInput();
        var controller = new SidebarScrollController(environment, input, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromMilliseconds(50));

        var result = await controller.RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.TimedOut, result.Status);
        Assert.True(input.CancellationObserved);
    }

    [Fact]
    public async Task Reveal_TimesOutWhenInputBlocksBeforeReturningATask()
    {
        var environment = FakeAutomationEnvironment.WithPages(0, Page("top"));
        var controller = new SidebarScrollController(environment, new BlockingScrollInput(), TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromMilliseconds(50));
        var started = Stopwatch.GetTimestamp();

        var result = await controller.RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.TimedOut, result.Status);
        Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Reveal_TimesOutDuringLongSettleDelay()
    {
        var environment = FakeAutomationEnvironment.WithPages(0, Page("top"));
        var controller = new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.FromSeconds(1), 80, TimeSpan.FromMilliseconds(50));

        var result = await controller.RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task Reveal_ProbeWaitsForTwoUnchangedSnapshotsBeforeFallback()
    {
        var environment = FakeAutomationEnvironment.WithDelayedDownUpdate(0, 1,
            Page("top"), Page("target", targetTitle: "Wanted"));
        var controller = new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8));

        var result = await controller.RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);

        Assert.Equal(SidebarScrollStatus.Found, result.Status);
        Assert.Equal([SidebarInputMode.AutomationPattern], environment.Modes.Distinct());
    }

    [Fact]
    public void Detect_RejectsListItemsFromDifferentSidebarContainers()
    {
        var snapshot = new AutomationSnapshot(new Rect(0, 0, 1000, 800),
        [
            Node("a", 20, ["root", "sidebar-a"]),
            Node("b", 60, ["root", "sidebar-b"]),
            Node("c", 100, ["root", "sidebar-a"]),
            Node("d", 140, ["root", "sidebar-b"])
        ]);

        Assert.Null(SidebarRegionDetector.Detect(snapshot));
    }

    [Fact]
    public void Detect_UsesAValidatedSidebarItemAsTheInputAnchor()
    {
        var snapshot = Page("top");

        var region = SidebarRegionDetector.Detect(snapshot);

        Assert.NotNull(region);
        var anchor = Assert.Single(snapshot.Nodes, node => node.RuntimeId == region.InputNodeRuntimeId);
        Assert.True(anchor.Bounds.Contains(region.InputPoint));
        Assert.Equal("sidebar", region.ContainerRuntimeId);
    }

    [Fact]
    public void Detect_RejectsSidebarsThatOnlyShareACommonHost()
    {
        var snapshot = new AutomationSnapshot(new Rect(0, 0, 1000, 800),
        [
            new AutomationNode("sidebar-a", "ControlType.List", "", "", new Rect(10, 20, 200, 160), false, ["root"], 0),
            new AutomationNode("sidebar-b", "ControlType.List", "", "", new Rect(240, 20, 200, 160), false, ["root"], 1),
            Node("a", 20, ["root", "sidebar-a"]),
            Node("b", 60, ["root", "sidebar-b"]),
            Node("c", 100, ["root", "sidebar-a"]),
            Node("d", 140, ["root", "sidebar-b"])
        ]);

        Assert.Null(SidebarRegionDetector.Detect(snapshot));
    }

    [Fact]
    public void Detect_RejectsAnAnchorWithoutTheSameHitTestWindowAsEverySidebarItem()
    {
        var snapshot = SidebarSnapshot(0, 55, 55, 55);

        Assert.Null(SidebarRegionDetector.Detect(snapshot));
    }

    [Fact]
    public async Task NativeInput_UsesOneWheelWithExpectedDirectionAndRestoresAfterSendFailure()
    {
        var api = new FakeNativeWheelApi { Foreground = 123, PointWindow = 55, SendWheelResult = false, ForegroundAfterSend = 456 };
        var input = new NativeSidebarWheelInput(api);
        var region = Region(expectedHitTestWindow: 55);

        var result = await input.ScrollAsync(123, region, ScrollDirection.Down, SidebarInputMode.PhysicalFallback, Permit(), default);

        Assert.False(result);
        Assert.Equal([-240], api.WheelDeltas);
        Assert.Equal(new Point(4, 5), api.Cursor);
        Assert.Equal((nint)123, api.Foreground);
    }

    [Fact]
    public async Task NativeInput_RejectsUnvalidatedPointWithoutWheel()
    {
        var api = new FakeNativeWheelApi { Foreground = 123, PointWindow = 99 };
        var input = new NativeSidebarWheelInput(api);

        var result = await input.ScrollAsync(123, Region(expectedHitTestWindow: 55), ScrollDirection.Up, SidebarInputMode.PostedMessage, Permit(), default);

        Assert.False(result);
        Assert.Empty(api.WheelDeltas);
        Assert.Empty(api.PostedDeltas);
    }

    [Fact]
    public async Task NativeInput_RejectsCancellationBeforeChangingCursor()
    {
        var api = new FakeNativeWheelApi { Foreground = 123, PointWindow = 55 };
        var input = new NativeSidebarWheelInput(api);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            input.ScrollAsync(123, Region(expectedHitTestWindow: 55), ScrollDirection.Up, SidebarInputMode.PhysicalFallback, Permit(), cancellation.Token));

        Assert.Empty(api.WheelDeltas);
        Assert.Equal(new Point(4, 5), api.Cursor);
    }

    [Fact]
    public async Task NativeInput_RestoresCursorAndForegroundWhenCancellationArrivesAfterCursorMove()
    {
        using var cancellation = new CancellationTokenSource();
        var api = new FakeNativeWheelApi { Foreground = 123, PointWindow = 55, CancelAfterCursorMove = cancellation };
        var input = new NativeSidebarWheelInput(api);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            input.ScrollAsync(123, Region(expectedHitTestWindow: 55), ScrollDirection.Down, SidebarInputMode.PhysicalFallback, Permit(), cancellation.Token));

        Assert.Empty(api.WheelDeltas);
        Assert.Equal(new Point(4, 5), api.Cursor);
        Assert.Equal((nint)123, api.Foreground);
    }

    [Fact]
    public async Task Reveal_ExpiresPermitBeforeABlockedInputCanPerformItsLateEffect()
    {
        var environment = FakeAutomationEnvironment.WithPages(0, Page("top"));
        var input = new BlockingBeforeEffectInput();
        var controller = new SidebarScrollController(environment, input, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromMilliseconds(50));

        var result = await controller.RevealAsync(123, new SidebarTarget("Wanted", SidebarThreadGroup.Projectless()), default);
        input.Release.Set();
        Assert.True(input.Finished.Wait(TimeSpan.FromSeconds(1)));

        Assert.Equal(SidebarScrollStatus.TimedOut, result.Status);
        Assert.Equal(0, input.EffectCount);
    }

    [Fact]
    public void Permit_AuthorizesOnlyItsFirstEffectBeforeDeadline()
    {
        var authorization = new ScrollEffectAuthorization(TimeProvider.System, TimeProvider.System.GetTimestamp(), TimeSpan.FromSeconds(1), default);
        var permit = authorization.CreatePermit();

        Assert.True(permit.TryAuthorize());
        Assert.False(permit.TryAuthorize());
    }

    private static AutomationSnapshot Page(string key, string? targetTitle = null, bool targetOffscreen = false)
    {
        var nodes = new List<AutomationNode>
        {
            new("sidebar", "ControlType.List", "", "", new Rect(10, 20, 200, 180), false, ["root"], 0)
        };
        for (var item = 0; item < 4; item++)
            nodes.Add(new($"{key}-{item}", "ControlType.ListItem", $"{key}-filler-{item}", "",
                new Rect(10, 20 + item * 40, 200, 30), false, ["root", "sidebar"], item + 1));
        if (targetTitle is not null)
            nodes.Add(new($"{key}-target", "ControlType.ListItem", targetTitle, "",
                targetOffscreen ? Rect.Empty : new Rect(10, 160, 200, 30), targetOffscreen,
                ["root", "sidebar"], 5));
        return new AutomationSnapshot(new Rect(0, 0, 1000, 800), nodes);
    }

    private static AutomationNode Node(string id, double top, string[] ancestors) =>
        new(id, "ControlType.ListItem", id, "", new Rect(10, top, 200, 30), false, ancestors, (int)top);

    private static AutomationSnapshot SidebarSnapshot(params nint[] itemWindows)
    {
        var nodes = new List<AutomationNode>
        {
            new("sidebar", "ControlType.List", "", "", new Rect(10, 20, 200, 160), false, ["root"], 0)
        };
        for (var index = 0; index < itemWindows.Length; index++)
            nodes.Add(new AutomationNode($"item-{index}", "ControlType.ListItem", "", "", new Rect(10, 20 + index * 40, 200, 30), false, ["root", "sidebar"], index + 1, itemWindows[index]));
        return new AutomationSnapshot(new Rect(0, 0, 1000, 800), nodes);
    }

    private static SidebarScrollRegion Region(nint expectedHitTestWindow) =>
        new(new Rect(10, 20, 200, 150), new Point(22, 75), "sidebar", "item", expectedHitTestWindow);

    private static IScrollEffectPermit Permit() =>
        new ScrollEffectAuthorization(TimeProvider.System, TimeProvider.System.GetTimestamp(), TimeSpan.FromSeconds(1), default).CreatePermit();

    private sealed class HangingSnapshotProvider : IUiAutomationSnapshotProvider
    {
        public bool CancellationObserved { get; private set; }

        public Task<AutomationSnapshot> CaptureAsync(nint windowHandle, CancellationToken token)
        {
            token.Register(() => CancellationObserved = true);
            return new TaskCompletionSource<AutomationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }

    private sealed class HangingScrollInput : ISidebarScrollInput
    {
        public bool CancellationObserved { get; private set; }

        public Task<bool> ScrollAsync(nint windowHandle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, IScrollEffectPermit permit, CancellationToken token)
        {
            token.Register(() => CancellationObserved = true);
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }

    private sealed class NeverCalledScrollInput : ISidebarScrollInput
    {
        public Task<bool> ScrollAsync(nint windowHandle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, IScrollEffectPermit permit, CancellationToken token) =>
            throw new InvalidOperationException("Input should not be called while snapshot is hung.");
    }

    private sealed class BlockingScrollInput : ISidebarScrollInput
    {
        public Task<bool> ScrollAsync(nint windowHandle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, IScrollEffectPermit permit, CancellationToken token)
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(500));
            return Task.FromResult(true);
        }
    }

    private sealed class BlockingBeforeEffectInput : ISidebarScrollInput
    {
        public ManualResetEventSlim Release { get; } = new();
        public ManualResetEventSlim Finished { get; } = new();
        public int EffectCount { get; private set; }

        public Task<bool> ScrollAsync(nint windowHandle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, IScrollEffectPermit permit, CancellationToken token)
        {
            Release.Wait();
            if (permit.TryAuthorize())
                EffectCount++;
            Finished.Set();
            return Task.FromResult(EffectCount > 0);
        }
    }

    private sealed class FakeNativeWheelApi : INativeSidebarWheelApi
    {
        public Point Cursor { get; private set; } = new(4, 5);
        public nint Foreground { get; set; }
        public nint PointWindow { get; set; }
        public nint ForegroundAfterSend { get; set; }
        public CancellationTokenSource? CancelAfterCursorMove { get; set; }
        public bool SendWheelResult { get; set; } = true;
        public List<int> WheelDeltas { get; } = [];
        public List<int> PostedDeltas { get; } = [];

        public bool GetCursorPosition(out Point point)
        {
            point = Cursor;
            return true;
        }

        public bool SetCursorPosition(Point point)
        {
            Cursor = point;
            CancelAfterCursorMove?.Cancel();
            return true;
        }

        public nint GetForegroundWindow() => Foreground;

        public bool SetForegroundWindow(nint handle)
        {
            Foreground = handle;
            return true;
        }

        public nint WindowFromPoint(Point point) => PointWindow;

        public bool IsWindowOwnedBy(nint root, nint window) => root == 123 && window is 55 or 99;

        public bool PostWheel(nint handle, Point point, int delta)
        {
            PostedDeltas.Add(delta);
            return true;
        }

        public bool SendWheel(int delta)
        {
            WheelDeltas.Add(delta);
            Foreground = ForegroundAfterSend == 0 ? Foreground : ForegroundAfterSend;
            return SendWheelResult;
        }
    }
}
