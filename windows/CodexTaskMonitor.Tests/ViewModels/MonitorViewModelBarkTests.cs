using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;
using CodexTaskMonitor.Windows.Notifications;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Tests.ViewModels;

public sealed class MonitorViewModelBarkTests
{
    [Fact]
    public async Task CommittedRefresh_ObservesItemsAndDismissRemovesExactItem()
    {
        var item = Item();
        var notifier = new RecordingNotifier();
        await using var model = Model(new StaticMonitor(item), notifier);

        await model.StartAsync(false, default);
        await model.DismissAsync(model.Items.Single(), default);

        Assert.Contains(notifier.Snapshots, snapshot => snapshot.Single().Id == item.Id);
        Assert.Equal([item.Id], notifier.RemovedIds);
    }

    [Fact]
    public async Task BarkWarning_IsLowerPriorityThanScanAndActionErrors()
    {
        var monitor = new StaticMonitor(Item());
        var notifier = new RecordingNotifier();
        await using var model = Model(monitor, notifier);
        await model.StartAsync(false, default);

        notifier.RaiseWarning("Bark 通知发送失败，将自动重试");
        await EventuallyAsync(() => model.ErrorMessage is not null);
        Assert.Equal("Bark 通知发送失败，将自动重试", model.ErrorMessage);

        monitor.Error = new InvalidOperationException("private");
        await model.RefreshAsync(default);
        Assert.Equal("暂时无法读取 Codex 数据", model.ErrorMessage);

        model.ReportActionFailure();
        Assert.Equal("暂时无法完成此操作，请重试", model.ErrorMessage);
    }

    [Fact]
    public async Task DisposeAsync_DisposesNotifier()
    {
        var notifier = new RecordingNotifier();
        var model = Model(new StaticMonitor(), notifier);

        await model.DisposeAsync();

        Assert.True(notifier.Disposed);
    }

    private static MonitorViewModel Model(ITaskMonitor monitor, ITaskCompletionNotifier notifier) =>
        new(
            monitor,
            new MemoryPreferences(new MonitorPreferences(
                DateTimeOffset.UtcNow.AddHours(-1), [], [], [], null, null, false)),
            new NullActivation(),
            new DisabledStartup(),
            new NoLaunchTime(),
            TimeProvider.System,
            notifier);

    private static MonitorItem Item() =>
        new(
            "thread", "turn", "Task title", @"C:\Project", "Project", DateTimeOffset.UtcNow,
            TaskState.Waiting, TaskTerminalKind.Completed);

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class StaticMonitor(params MonitorItem[] items) : ITaskMonitor
    {
        public Exception? Error { get; set; }

        public Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken token) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken token) =>
            Error is null
                ? Task.FromResult(new MonitorScanResult(items, 0))
                : Task.FromException<MonitorScanResult>(Error);
    }

    private sealed class MemoryPreferences(MonitorPreferences value) : IMonitorPreferencesStore
    {
        private MonitorPreferences current = value;

        public Task<MonitorPreferences> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(current);

        public Task SaveAsync(MonitorPreferences preferences, CancellationToken cancellationToken)
        {
            current = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNotifier : ITaskCompletionNotifier
    {
        public event EventHandler<string?>? WarningChanged;
        public List<IReadOnlyList<MonitorItem>> Snapshots { get; } = [];
        public List<string> RemovedIds { get; } = [];
        public bool Disposed { get; private set; }

        public void Observe(IReadOnlyList<MonitorItem> items) => Snapshots.Add(items.ToArray());

        public void Remove(string itemId) => RemovedIds.Add(itemId);

        public void RaiseWarning(string? warning) => WarningChanged?.Invoke(this, warning);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NullActivation : IThreadActivationService
    {
        public Task<string?> ActivateAsync(MonitorItem item, CancellationToken token) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DisabledStartup : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private sealed class NoLaunchTime : ICodexLaunchTimeProvider
    {
        public DateTimeOffset? GetLaunchTime() => null;
    }
}
