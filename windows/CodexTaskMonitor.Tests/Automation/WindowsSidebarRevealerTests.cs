using System.Diagnostics;
using System.Windows;
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Sidebar;
using CodexTaskMonitor.Tests.Fakes;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Tests.Automation;

public sealed class WindowsSidebarRevealerTests
{
    [Fact]
    public async Task Reveal_WaitsUntilBothHandleAndUiaRootAreReady()
    {
        var probe = new DelayedRootProbe(failuresBeforeReady: 2);
        var revealer = Create(probe);

        var result = await revealer.RevealAsync(Item(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, probe.Attempts);
    }

    [Fact]
    public async Task Reveal_RootThatNeverBecomesReady_UsesTheFiveSecondReadinessBound()
    {
        var revealer = Create(new DelayedRootProbe(int.MaxValue));
        var started = Stopwatch.GetTimestamp();

        var result = await revealer.RevealAsync(Item(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.InRange(Stopwatch.GetElapsedTime(started), TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task Reveal_CancellationDuringRootReadiness_PropagatesToTheCaller()
    {
        var probe = new BlockingRootProbe();
        var revealer = Create(probe);
        using var cancellation = new CancellationTokenSource();

        var reveal = revealer.RevealAsync(Item(), cancellation.Token);
        await probe.Started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reveal);
    }

    [Fact]
    public async Task Reveal_AfterRootReadiness_UsesTheScrollerWithoutChargingReadinessRetriesToItsBudget()
    {
        var environment = FakeAutomationEnvironment.WithPages(0, SidebarPage("Wanted"));
        var scroller = new SidebarScrollController(environment, environment, TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8));
        var probe = new DelayedRootProbe(failuresBeforeReady: 2);
        var paths = CreateResolutionFiles();
        try
        {
            var revealer = new WindowsSidebarRevealer(
                new FixedWindowLocator(123), scroller, paths.SessionIndex, paths.GlobalState,
                new FixedGrouping(), TimeProvider.System, probe);

            var result = await revealer.RevealAsync(Item(), CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(3, probe.Attempts);
            Assert.Empty(environment.Actions);
        }
        finally
        {
            Directory.Delete(paths.Directory, recursive: true);
        }
    }

    private static WindowsSidebarRevealer Create(IUiAutomationRootReadinessProbe probe) =>
        new(new FixedWindowLocator(123),
            new SidebarScrollController(
                FakeAutomationEnvironment.WithPages(0, new AutomationSnapshot(new Rect(0, 0, 1, 1), [])),
                FakeAutomationEnvironment.WithPages(0, new AutomationSnapshot(new Rect(0, 0, 1, 1), [])),
                TimeProvider.System, TimeSpan.Zero, 80, TimeSpan.FromSeconds(8)),
            "not-read", "not-read", new MissingGrouping(), TimeProvider.System, probe);

    private static MonitorItem Item() => new("thread", "turn", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Waiting);

    private static AutomationSnapshot SidebarPage(string title) => new(new Rect(0, 0, 1000, 800),
    [
        new AutomationNode("sidebar", "ControlType.List", "", "", new Rect(10, 20, 200, 180), false, ["root"], 0),
        new AutomationNode("pinned", "ControlType.Text", "\u7F6E\u9876", "", new Rect(10, 20, 200, 20), false, ["root", "sidebar"], 1),
        new AutomationNode("target", "ControlType.ListItem", title, "", new Rect(10, 50, 200, 30), false, ["root", "sidebar"], 2)
    ]);

    private static (string Directory, string SessionIndex, string GlobalState) CreateResolutionFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CodexTaskMonitor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sessionIndex = Path.Combine(directory, "session_index.jsonl");
        var globalState = Path.Combine(directory, "global-state.json");
        File.WriteAllText(sessionIndex, "{\"id\":\"thread\",\"thread_name\":\"Wanted\"}\n");
        File.WriteAllText(globalState, "{}");
        return (directory, sessionIndex, globalState);
    }

    private sealed class FixedWindowLocator(nint handle) : IChatGptWindowLocator
    {
        public nint FindMainWindow() => handle;
    }

    private sealed class MissingGrouping : IThreadGroupingLookup
    {
        public Task<ThreadGroupingInfo?> FindGroupingAsync(string threadId, CancellationToken cancellationToken) =>
            Task.FromResult<ThreadGroupingInfo?>(null);
    }

    private sealed class FixedGrouping : IThreadGroupingLookup
    {
        public Task<ThreadGroupingInfo?> FindGroupingAsync(string threadId, CancellationToken cancellationToken) =>
            Task.FromResult<ThreadGroupingInfo?>(new ThreadGroupingInfo(IsPinned: true, SectionName: null, Cwd: @"C:\work"));
    }

    private sealed class DelayedRootProbe(int failuresBeforeReady) : IUiAutomationRootReadinessProbe
    {
        private int remaining = failuresBeforeReady;
        public int Attempts { get; private set; }

        public Task ProbeAsync(nint windowHandle, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Attempts++;
            if (remaining-- > 0)
                throw new UiAutomationRootUnavailableException();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingRootProbe : IUiAutomationRootReadinessProbe
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ProbeAsync(nint windowHandle, CancellationToken token)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }
}
