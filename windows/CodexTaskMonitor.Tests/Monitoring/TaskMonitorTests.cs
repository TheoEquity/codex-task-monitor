using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Tests.Monitoring;

public sealed class TaskMonitorTests
{
    [Fact]
    public async Task AppendedCompletion_ChangesRunningItemToWaiting()
    {
        var path = await WriteTemporaryRolloutAsync(Started("turn-1", 101));
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("thread-1", path)));

            Assert.Equal(TaskState.Running, (await monitor.ScanAsync(Options(), default)).Items.Single().State);
            await File.AppendAllTextAsync(path, Completed("turn-1", 101, 102));

            Assert.Equal(TaskState.Waiting, (await monitor.ScanAsync(Options(), default)).Items.Single().State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnterminatedTail_IsNotConsumedAndIsReadFromItsTrueStartAfterAppend()
    {
        var path = await WriteTemporaryRolloutAsync(
            Started("turn-1", 101) +
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-1\",\"started_at\":101,\"completed_at\":102,\"padding\":\"" +
            new string('x', 70 * 1024));
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));

            Assert.Equal(TaskState.Running, (await monitor.ScanAsync(Options(), default)).Items.Single().State);
            await File.AppendAllTextAsync(path, "\"}}\n");

            Assert.Equal(TaskState.Waiting, (await monitor.ScanAsync(Options(), default)).Items.Single().State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnterminatedFirstLifecycleLine_IsReadAfterItsTerminatorArrives()
    {
        var path = await WriteTemporaryRolloutAsync(
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\",\"started_at\":101");
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));

            Assert.Empty((await monitor.ScanAsync(Options(), default)).Items);
            await File.AppendAllTextAsync(path, "}}\n");

            Assert.Equal("turn-1", (await monitor.ScanAsync(Options(), default)).Items.Single().TurnId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TruncatedOrRotatedFile_IsFullyReparsed()
    {
        var path = await WriteTemporaryRolloutAsync(Started("turn-before", 101) + new string('x', 1024) + "\n");
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));
            await monitor.ScanAsync(Options(), default);

            await File.WriteAllTextAsync(path, Started("turn-after-truncate", 103));

            Assert.Equal("turn-after-truncate", (await monitor.ScanAsync(Options(), default)).Items.Single().TurnId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReplacedRollout_IsNotTreatedAsAnAppend()
    {
        var path = await WriteTemporaryRolloutAsync(Started("turn-before", 101));
        var replacement = await WriteTemporaryRolloutAsync(Started("turn-after-rotation", 103) + new string('x', 1024) + "\n");
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));
            await monitor.ScanAsync(Options(), default);

            File.Move(replacement, path, overwrite: true);
            replacement = string.Empty;

            Assert.Equal("turn-after-rotation", (await monitor.ScanAsync(Options(), default)).Items.Single().TurnId);
        }
        finally
        {
            File.Delete(path);
            if (!string.IsNullOrEmpty(replacement))
                File.Delete(replacement);
        }
    }

    [Fact]
    public async Task TemporarilyMissingRollout_UsesCacheAndReportsCount()
    {
        var path = await WriteTemporaryRolloutAsync(Started("turn-1", 101));
        var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));
        await monitor.ScanAsync(Options(), default);
        File.Delete(path);

        var result = await monitor.ScanAsync(Options(), default);

        Assert.Single(result.Items);
        Assert.Equal(1, result.UnreadableRolloutCount);
    }

    [Fact]
    public async Task AllUnreadableRollouts_PropagatesPrivacySafeUnreadableError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jsonl");
        var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));

        var error = await Assert.ThrowsAsync<CodexDataException>(() => monitor.ScanAsync(Options(), default));

        Assert.Equal(CodexDataError.Unreadable, error.Error);
        Assert.DoesNotContain(path, error.Message);
    }

    [Fact]
    public async Task CurrentlyRunningTurnIds_ReturnsOnlyRunningTurns()
    {
        var path = await WriteTemporaryRolloutAsync(Started("turn-running", 101));
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));

            Assert.Equal(["turn-running"], await monitor.CurrentlyRunningTurnIdsAsync(Baseline, default));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExactDismissedItem_DoesNotHideSameTurnInAnotherThread()
    {
        var first = await WriteTemporaryRolloutAsync(Completed("turn-1", 101, 102));
        var second = await WriteTemporaryRolloutAsync(Completed("turn-1", 101, 102));
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("thread-a", first), Record("thread-b", second)));

            var result = await monitor.ScanAsync(Options(dismissedItems: new HashSet<string> { "thread-a:turn-1" }), default);

            Assert.Equal(["thread-b"], result.Items.Select(item => item.ThreadId));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public async Task LegacyDismissedTurnId_HidesTerminalItemsButNotRunningItems()
    {
        var completed = await WriteTemporaryRolloutAsync(Completed("turn-shared", 101, 102));
        var running = await WriteTemporaryRolloutAsync(Started("turn-shared", 103));
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("complete", completed), Record("running", running)));

            var result = await monitor.ScanAsync(Options(dismissedTurns: new HashSet<string> { "turn-shared" }), default);

            Assert.Equal(["running"], result.Items.Select(item => item.ThreadId));
        }
        finally
        {
            File.Delete(completed);
            File.Delete(running);
        }
    }

    [Fact]
    public async Task Items_AreSortedByLifecycleActivityDescending()
    {
        var older = await WriteTemporaryRolloutAsync(Started("old", 101));
        var newer = await WriteTemporaryRolloutAsync(Started("new", 102));
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("older", older), Record("newer", newer)));

            Assert.Equal(["newer", "older"], (await monitor.ScanAsync(Options(), default)).Items.Select(item => item.ThreadId));
        }
        finally
        {
            File.Delete(older);
            File.Delete(newer);
        }
    }

    [Fact]
    public async Task CompleteLifecycleLineWithMissingFields_ReportsFormatChange()
    {
        var path = await WriteTemporaryRolloutAsync("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n");
        try
        {
            var monitor = new TaskMonitor(new FakeThreadStore(Record("thread", path)));

            var error = await Assert.ThrowsAsync<CodexDataException>(() => monitor.ScanAsync(Options(), default));

            Assert.Equal(CodexDataError.FormatChanged, error.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static readonly DateTimeOffset Baseline = DateTimeOffset.FromUnixTimeSeconds(100);

    private static ThreadRecord Record(string id, string path) =>
        new(id, id, @"C:\work", DateTimeOffset.FromUnixTimeSeconds(123), path,
            new ThreadGroupingInfo(false, null, @"C:\work"));

    private static MonitorScanOptions Options(
        IReadOnlySet<string>? dismissedTurns = null,
        IReadOnlySet<string>? dismissedItems = null) =>
        new(Baseline, new HashSet<string>(), dismissedTurns ?? new HashSet<string>(), dismissedItems ?? new HashSet<string>());

    private static string Started(string turnId, long startedAt) =>
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"" + turnId + "\",\"started_at\":" + startedAt + "}}\n";

    private static string Completed(string turnId, long startedAt, long completedAt) =>
        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"" + turnId + "\",\"started_at\":" + startedAt + ",\"completed_at\":" + completedAt + "}}\n";

    private static async Task<string> WriteTemporaryRolloutAsync(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"monitor-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, contents);
        return path;
    }

    private sealed class FakeThreadStore(params ThreadRecord[] records) : IThreadStore
    {
        public Task<IReadOnlyList<ThreadRecord>> ReadThreadsAsync(DateTimeOffset updatedAfter, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ThreadRecord>>(records.Where(record => record.UpdatedAt >= updatedAfter).ToArray());
    }
}
