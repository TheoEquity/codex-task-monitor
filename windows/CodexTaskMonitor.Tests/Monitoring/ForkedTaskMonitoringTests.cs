using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Tests.Fixtures;

namespace CodexTaskMonitor.Tests.Monitoring;

public sealed class ForkedTaskMonitoringTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.FromUnixTimeSeconds(100);

    [Theory]
    [InlineData("task_started", TaskState.Running)]
    [InlineData("task_complete", TaskState.Waiting)]
    [InlineData("turn_aborted", TaskState.Waiting)]
    public async Task VisibleFork_UsesNormalLifecycleState(string eventType, TaskState expectedState)
    {
        await using var fixture = await CodexFixture.CreateAsync();
        var rollout = await WriteRolloutAsync(fixture, "fork", LifecycleLine(eventType, "fork-turn"));
        await fixture.InsertThreadAsync(
            "fork-thread", "Shared title", "subagent", "vscode",
            archived: false, preview: "visible", rolloutPath: rollout);
        var monitor = new TaskMonitor(new SqliteThreadStore(fixture.DatabasePath));

        var result = await monitor.ScanAsync(Options(), default);

        var item = Assert.Single(result.Items);
        Assert.Equal("fork-thread", item.ThreadId);
        Assert.Equal("fork-turn", item.TurnId);
        Assert.Equal(expectedState, item.State);
    }

    [Fact]
    public async Task AbortedVisibleForkWithRootTimestamp_DoesNotBlockNormalTasks()
    {
        await using var fixture = await CodexFixture.CreateAsync();
        var userRollout = await WriteRolloutAsync(
            fixture, "user", LifecycleLine("task_started", "user-turn"));
        var forkRollout = await WriteRolloutAsync(
            fixture,
            "fork-aborted",
            "{\"type\":\"event_msg\",\"timestamp\":\"1970-01-01T00:01:42Z\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"fork-turn\",\"started_at\":101,\"reason\":\"interrupted\"}}\n");
        await fixture.InsertThreadAsync(
            "user-thread", "Normal", "user", "vscode",
            archived: false, preview: "visible", rolloutPath: userRollout);
        await fixture.InsertThreadAsync(
            "fork-thread", "Fork", "subagent", "vscode",
            archived: false, preview: "visible", rolloutPath: forkRollout);
        var monitor = new TaskMonitor(new SqliteThreadStore(fixture.DatabasePath));

        var result = await monitor.ScanAsync(Options(), default);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, item => item.Id == "user-thread:user-turn" && item.State == TaskState.Running);
        Assert.Contains(result.Items, item => item.Id == "fork-thread:fork-turn" && item.State == TaskState.Waiting);
        Assert.Equal(0, result.UnreadableRolloutCount);
    }

    [Fact]
    public async Task DismissingParent_DoesNotHideForkWithSameTitleAndTurnId()
    {
        await using var fixture = await CodexFixture.CreateAsync();
        var parentRollout = await WriteRolloutAsync(fixture, "parent", LifecycleLine("task_complete", "shared-turn"));
        var forkRollout = await WriteRolloutAsync(fixture, "fork", LifecycleLine("task_complete", "shared-turn"));
        await fixture.InsertThreadAsync(
            "parent-thread", "Shared title", "user", "vscode",
            archived: false, preview: "visible", rolloutPath: parentRollout);
        await fixture.InsertThreadAsync(
            "fork-thread", "Shared title", "subagent", "vscode",
            archived: false, preview: "visible", rolloutPath: forkRollout);
        var monitor = new TaskMonitor(new SqliteThreadStore(fixture.DatabasePath));
        var options = Options(new HashSet<string>(StringComparer.Ordinal) { "parent-thread:shared-turn" });

        var result = await monitor.ScanAsync(options, default);

        var item = Assert.Single(result.Items);
        Assert.Equal("fork-thread:shared-turn", item.Id);
        Assert.Equal("Shared title", item.Title);
    }

    private static MonitorScanOptions Options(IReadOnlySet<string>? dismissedItems = null) =>
        new(
            Baseline,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            dismissedItems ?? new HashSet<string>(StringComparer.Ordinal));

    private static string LifecycleLine(string eventType, string turnId) =>
        eventType == "task_started"
            ? "{\"type\":\"event_msg\",\"payload\":{\"type\":\"" + eventType +
              "\",\"turn_id\":\"" + turnId + "\",\"started_at\":101}}\n"
            : "{\"type\":\"event_msg\",\"payload\":{\"type\":\"" + eventType +
              "\",\"turn_id\":\"" + turnId + "\",\"started_at\":101,\"completed_at\":102}}\n";

    private static async Task<string> WriteRolloutAsync(CodexFixture fixture, string name, string contents)
    {
        var root = Path.GetDirectoryName(fixture.DatabasePath)!;
        var path = Path.Combine(root, $"{name}.jsonl");
        await File.WriteAllTextAsync(path, contents);
        return path;
    }
}
