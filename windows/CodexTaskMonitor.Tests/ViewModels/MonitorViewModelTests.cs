using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Tests.ViewModels;

public sealed class MonitorViewModelTests
{
    [Fact]
    public async Task Start_AdoptsRecentRunningTurnsAndPublishesItems()
    {
        var monitor = new FakeTaskMonitor(
            Set("turn-adopted"),
            [new MonitorItem("thread", "turn-adopted", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Running)]);
        var preferences = new FakePreferencesStore(MonitorPreferences.Empty);
        var viewModel = Create(monitor, preferences, launchTime: DateTimeOffset.UtcNow.AddMinutes(-10));

        await viewModel.StartAsync(startPollingLoop: false, CancellationToken.None);

        Assert.Single(viewModel.Items);
        Assert.Contains("turn-adopted", preferences.Value.AdoptedTurnIds);
        Assert.True(viewModel.PanelHeight >= 48);
    }

    [Fact]
    public async Task Dismiss_PersistsExactWaitingItemAndRefreshes()
    {
        var item = new MonitorItem("thread", "turn", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Waiting);
        var monitor = new FakeTaskMonitor(Set(), [item]);
        var preferences = new FakePreferencesStore(new(DateTimeOffset.UtcNow.AddHours(-1), [], [], [], null, null, true));
        var viewModel = Create(monitor, preferences);
        await viewModel.StartAsync(false, CancellationToken.None);

        await viewModel.DismissAsync(viewModel.Items.Single(), CancellationToken.None);

        Assert.Contains("thread:turn", preferences.Value.DismissedItemIds);
    }

    [Fact]
    public async Task Dismiss_DoesNotPersistRunningItem()
    {
        var item = new MonitorItem("thread", "turn", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Running);
        var preferences = new FakePreferencesStore(new(DateTimeOffset.UtcNow.AddHours(-1), [], [], [], null, null, true));
        var viewModel = Create(new FakeTaskMonitor(Set(), [item]), preferences);
        await viewModel.StartAsync(false, CancellationToken.None);

        await viewModel.DismissAsync(viewModel.Items.Single(), CancellationToken.None);

        Assert.Empty(preferences.Value.DismissedItemIds);
    }

    [Fact]
    public async Task ToggleStartup_PersistsDisabledChoiceAcrossRestarts()
    {
        var preferences = new FakePreferencesStore(MonitorPreferences.Empty);
        var startup = new FakeStartup(enabled: true);
        var viewModel = Create(new FakeTaskMonitor(Set(), []), preferences, startup: startup);
        await viewModel.StartAsync(false, CancellationToken.None);

        await viewModel.ToggleStartupAsync(CancellationToken.None);

        Assert.False(viewModel.IsStartupEnabled);
        Assert.Equal(false, preferences.Value.LaunchAtLoginEnabled);
        Assert.False(startup.IsEnabled);
    }

    [Fact]
    public async Task Refresh_DatabaseMissingShowsLocalErrorAndUsesLongRetryDelay()
    {
        var monitor = new FakeTaskMonitor(Set(), []) { ScanException = new CodexDataException(CodexDataError.DatabaseMissing, "missing") };
        var viewModel = Create(monitor, new FakePreferencesStore(new(DateTimeOffset.UtcNow, [], [], [], null, null, true)));

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal("未找到本机 Codex 数据", viewModel.ErrorMessage);
        Assert.Equal(TimeSpan.FromSeconds(10), viewModel.NextPollDelay);
    }

    [Fact]
    public async Task Refresh_NewerGenerationCancelsOldScanAndPublishesOnlyLatestItems()
    {
        var initial = new MonitorItem("thread", "initial", "Initial", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Running);
        var latest = new MonitorItem("thread", "latest", "Latest", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Waiting);
        var firstScan = new TaskCompletionSource<MonitorScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new FakeTaskMonitor(Set(), []) { FirstScan = firstScan };
        var preferences = new FakePreferencesStore(new(DateTimeOffset.UtcNow, [], [], [], null, null, true));
        var viewModel = Create(monitor, preferences);

        var firstRefresh = viewModel.RefreshAsync(CancellationToken.None);
        await monitor.FirstScanStarted.Task;
        monitor.NextScanResult = new MonitorScanResult([latest], 0);
        var secondRefresh = viewModel.RefreshAsync(CancellationToken.None);
        await monitor.FirstScanCancelled.Task;
        firstScan.TrySetResult(new MonitorScanResult([initial], 0));

        await firstRefresh;
        await secondRefresh;

        Assert.DoesNotContain(viewModel.Items, item => item.Id == initial.Id);
        Assert.Collection(viewModel.Items, item => Assert.Equal(latest.Id, item.Id));
    }

    [Fact]
    public void ItemProjection_UsesAccessibleChineseStateAndDismissCapability()
    {
        var running = new MonitorItemViewModel(new MonitorItem("thread", "running", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Running));
        var waiting = new MonitorItemViewModel(new MonitorItem("thread", "waiting", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Waiting));

        Assert.Equal("运行中", running.StateText);
        Assert.Equal("#3B82F6", running.DotColor);
        Assert.False(running.CanDismiss);
        Assert.Equal("等待处理", waiting.StateText);
        Assert.Equal("#22C55E", waiting.DotColor);
        Assert.True(waiting.CanDismiss);
    }

    private static MonitorViewModel Create(
        ITaskMonitor monitor,
        IMonitorPreferencesStore preferences,
        IStartupRegistration? startup = null,
        DateTimeOffset? launchTime = null) =>
        new(monitor, preferences, new FakeActivation(), startup ?? new FakeStartup(), new FakeLaunchTime(launchTime), TimeProvider.System);

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private sealed class FakeTaskMonitor(
        IReadOnlySet<string> adopted,
        IReadOnlyList<MonitorItem> items) : ITaskMonitor
    {
        public Exception? ScanException { get; init; }

        public TaskCompletionSource<MonitorScanResult>? FirstScan { get; set; }

        public TaskCompletionSource<bool> FirstScanStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> FirstScanCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MonitorScanResult? NextScanResult { get; set; }

        public Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken token) =>
            Task.FromResult(adopted);

        public async Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken token)
        {
            if (FirstScan is { } firstScan)
            {
                FirstScan = null;
                FirstScanStarted.TrySetResult(true);
                using var registration = token.Register(() => FirstScanCancelled.TrySetResult(true));
                return await firstScan.Task;
            }

            if (ScanException is not null)
                throw ScanException;

            return NextScanResult ?? new MonitorScanResult(items, 0);
        }
    }

    private sealed class FakePreferencesStore(MonitorPreferences initial) : IMonitorPreferencesStore
    {
        public MonitorPreferences Value { get; private set; } = initial;

        public Task<MonitorPreferences> LoadAsync(CancellationToken token) => Task.FromResult(Value);

        public Task SaveAsync(MonitorPreferences value, CancellationToken token)
        {
            Value = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeActivation : IThreadActivationService
    {
        public Task<string?> ActivateAsync(MonitorItem item, CancellationToken token) => Task.FromResult<string?>(null);
    }

    private sealed class FakeStartup(bool enabled = false) : IStartupRegistration
    {
        public bool IsEnabled { get; private set; } = enabled;

        public void SetEnabled(bool value) => IsEnabled = value;
    }

    private sealed class FakeLaunchTime(DateTimeOffset? value) : ICodexLaunchTimeProvider
    {
        public DateTimeOffset? GetLaunchTime() => value;
    }
}
