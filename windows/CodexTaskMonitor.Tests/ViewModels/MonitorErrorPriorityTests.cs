using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Tests.ViewModels;

public sealed class MonitorErrorPriorityTests
{
    [Fact]
    public async Task RefreshCannotEraseActionWarning_AndLaterSuccessClearsIt()
    {
        var item = new MonitorItem("thread", "turn", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Waiting);
        var activation = new SequencedActivation("已打开对话；暂时无法在侧栏定位", null);
        var model = new MonitorViewModel(
            new StaticMonitor(item),
            new MemoryPreferences(new(DateTimeOffset.UtcNow.AddHours(-1), [], [], [], null, null, false)),
            activation, new DisabledStartup(), new NoLaunchTime(), TimeProvider.System);
        await model.StartAsync(false, CancellationToken.None);

        await model.OpenAsync(model.Items.Single(), CancellationToken.None);
        await model.RefreshAsync(CancellationToken.None);
        Assert.Equal("已打开对话；暂时无法在侧栏定位", model.ErrorMessage);

        await model.OpenAsync(model.Items.Single(), CancellationToken.None);
        Assert.Null(model.ErrorMessage);
    }

    private sealed class StaticMonitor(params MonitorItem[] items) : ITaskMonitor
    {
        public Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken token) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken token) =>
            Task.FromResult(new MonitorScanResult(items, 0));
    }

    private sealed class MemoryPreferences(MonitorPreferences value) : IMonitorPreferencesStore
    {
        private MonitorPreferences current = value;

        public Task<MonitorPreferences> LoadAsync(CancellationToken token) => Task.FromResult(current);

        public Task SaveAsync(MonitorPreferences preferences, CancellationToken token)
        {
            current = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class SequencedActivation(params string?[] messages) : IThreadActivationService
    {
        private readonly Queue<string?> queue = new(messages);

        public Task<string?> ActivateAsync(MonitorItem item, CancellationToken token) => Task.FromResult(queue.Dequeue());
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
