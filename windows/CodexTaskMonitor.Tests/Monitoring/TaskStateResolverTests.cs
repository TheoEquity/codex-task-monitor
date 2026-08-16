using CodexTaskMonitor.Core.Monitoring;

namespace CodexTaskMonitor.Tests.Monitoring;

public sealed class TaskStateResolverTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.FromUnixTimeSeconds(100);
    private static readonly IReadOnlySet<string> EmptyTurnIds = new HashSet<string>();

    [Fact]
    public void StartedAfterBaseline_IsRunning() =>
        Assert.Equal(TaskState.Running, TaskStateResolver.Resolve(
            new LifecycleEvent(LifecycleKind.Started, "turn-1", DateTimeOffset.FromUnixTimeSeconds(101), null),
            Baseline, EmptyTurnIds, EmptyTurnIds));

    [Fact]
    public void CompletedAfterBaseline_IsWaiting() =>
        Assert.Equal(TaskState.Waiting, TaskStateResolver.Resolve(
            new LifecycleEvent(LifecycleKind.Completed, "turn-1", DateTimeOffset.FromUnixTimeSeconds(99), DateTimeOffset.FromUnixTimeSeconds(101)),
            Baseline, EmptyTurnIds, EmptyTurnIds));

    [Fact]
    public void HandledExactTurn_IsHidden() =>
        Assert.Null(TaskStateResolver.Resolve(
            new LifecycleEvent(LifecycleKind.Aborted, "turn-1", DateTimeOffset.FromUnixTimeSeconds(101), DateTimeOffset.FromUnixTimeSeconds(102)),
            Baseline, EmptyTurnIds, new HashSet<string> { "turn-1" }));

    [Fact]
    public void AdoptedOldRunningTurn_IsRunning() =>
        Assert.Equal(TaskState.Running, TaskStateResolver.Resolve(
            new LifecycleEvent(LifecycleKind.Started, "turn-old", DateTimeOffset.FromUnixTimeSeconds(90), null),
            Baseline, new HashSet<string> { "turn-old" }, EmptyTurnIds));
}
