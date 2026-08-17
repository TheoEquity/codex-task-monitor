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
    public async Task ParentAndForkWithSameTitle_AreIndependentNormalPanelItems()
    {
        var parent = new MonitorItem(
            "parent-thread", "shared-turn", "Shared title", @"C:\work", "work",
            DateTimeOffset.UtcNow, TaskState.Waiting);
        var fork = new MonitorItem(
            "fork-thread", "shared-turn", "Shared title", @"C:\work", "work",
            DateTimeOffset.UtcNow.AddSeconds(1), TaskState.Waiting);
        var monitor = new FakeTaskMonitor(Set(), [fork, parent]);
        var preferences = new FakePreferencesStore(
            new MonitorPreferences(DateTimeOffset.UtcNow.AddHours(-1), [], [], [], null, null, true));
        var activation = new FakeActivation();
        var viewModel = new MonitorViewModel(
            monitor, preferences, activation, new FakeStartup(), new FakeLaunchTime(null), TimeProvider.System);
        await viewModel.StartAsync(startPollingLoop: false, CancellationToken.None);

        Assert.Equal(
            ["fork-thread:shared-turn", "parent-thread:shared-turn"],
            viewModel.Items.Select(item => item.Id).ToArray());
        Assert.All(viewModel.Items, item =>
        {
            Assert.Equal("Shared title", item.Title);
            Assert.True(item.CanDismiss);
        });

        var forkItem = viewModel.Items.Single(item => item.Item.ThreadId == "fork-thread");
        await viewModel.OpenAsync(forkItem, CancellationToken.None);
        await viewModel.DismissAsync(forkItem, CancellationToken.None);

        Assert.Equal("fork-thread:shared-turn", activation.LastItem!.Id);
        Assert.Contains("fork-thread:shared-turn", preferences.Value.DismissedItemIds);
        Assert.DoesNotContain("parent-thread:shared-turn", preferences.Value.DismissedItemIds);
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
    public async Task Dispose_WhileRefreshIsActive_CancelsAndAwaitsTheRefresh()
    {
        var firstScan = new TaskCompletionSource<MonitorScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new FakeTaskMonitor(Set(), []) { FirstScan = firstScan, FirstScanHonorsCancellation = true };
        var viewModel = Create(monitor, new FakePreferencesStore(new(DateTimeOffset.UtcNow, [], [], [], null, null, true)));

        var refresh = viewModel.RefreshAsync(CancellationToken.None);
        await monitor.FirstScanStarted.Task;

        await viewModel.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
    }

    [Fact]
    public async Task Open_ActivationFailureShowsRecoverablePrivacySafeError()
    {
        var activation = new FakeActivation { Exception = new InvalidOperationException("secret detail") };
        var viewModel = new MonitorViewModel(
            new FakeTaskMonitor(Set(), []),
            new FakePreferencesStore(new(DateTimeOffset.UtcNow, [], [], [], null, null, true)),
            activation,
            new FakeStartup(),
            new FakeLaunchTime(null),
            TimeProvider.System);
        var item = new MonitorItemViewModel(new MonitorItem("thread", "turn", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Running));

        await viewModel.OpenAsync(item, CancellationToken.None);

        Assert.Equal("暂时无法完成此操作，请重试", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AsyncCommand_FailureReportsSafeErrorWithoutEscapingAsyncVoid()
    {
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncCommand(
            () => Task.FromException(new InvalidOperationException("secret detail")),
            onError: () => reported.TrySetResult());

        command.Execute(null);

        await reported.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Refresh_NewGenerationBeforeBaselineCommit_PersistsOnlyNewBaseline()
    {
        var firstAdoption = new TaskCompletionSource<IReadOnlySet<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new FakeTaskMonitor(Set(), []) { FirstAdoption = firstAdoption, NextAdoption = Set("new-turn") };
        var preferences = new FakePreferencesStore(MonitorPreferences.Empty);
        Task? newerRefresh = null;
        MonitorViewModel? viewModel = null;
        var hook = new CommitHook(point =>
        {
            if (point == RefreshCommitPoint.Baseline)
                newerRefresh ??= viewModel!.RefreshAsync(CancellationToken.None);
        });
        viewModel = Create(monitor, preferences, commitHook: hook);

        var oldRefresh = viewModel.RefreshAsync(CancellationToken.None);
        await monitor.FirstAdoptionStarted.Task;
        firstAdoption.TrySetResult(Set("old-turn"));
        await oldRefresh;
        await newerRefresh!;

        Assert.Contains("new-turn", preferences.Value.AdoptedTurnIds);
        Assert.DoesNotContain("old-turn", preferences.Value.AdoptedTurnIds);
    }

    [Fact]
    public async Task Refresh_NewGenerationDuringBlockedBaselineSave_AdoptsAndPersistsOnlyNewGeneration()
    {
        var firstAdoption = new TaskCompletionSource<IReadOnlySet<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new FakeTaskMonitor(Set(), []) { FirstAdoption = firstAdoption, NextAdoption = Set("new-turn") };
        var preferences = new BlockingPreferencesStore(MonitorPreferences.Empty);
        var viewModel = Create(monitor, preferences);

        var oldRefresh = viewModel.RefreshAsync(CancellationToken.None);
        await monitor.FirstAdoptionStarted.Task;
        firstAdoption.TrySetResult(Set("old-turn"));
        await preferences.FirstSaveStarted.Task;

        await viewModel.RefreshAsync(CancellationToken.None);
        await oldRefresh;

        Assert.Contains("new-turn", preferences.Value.AdoptedTurnIds);
        Assert.DoesNotContain("old-turn", preferences.Value.AdoptedTurnIds);
        Assert.Contains("new-turn", monitor.LastScanOptions!.AdoptedTurnIds);
        Assert.DoesNotContain("old-turn", monitor.LastScanOptions.AdoptedTurnIds);
    }

    [Fact]
    public async Task Refresh_BlockedBaselineSaveThenToggle_RebasesOnTheCommittedBaseline()
    {
        var monitor = new FakeTaskMonitor(Set("adopted"), []);
        var preferences = new CommitBlockingPreferencesStore(MonitorPreferences.Empty);
        var startup = new FakeStartup(enabled: true);
        var viewModel = Create(monitor, preferences, startup: startup);

        var refresh = viewModel.RefreshAsync(CancellationToken.None);
        await preferences.FirstSaveStarted.Task;
        var toggle = viewModel.ToggleStartupAsync(CancellationToken.None);
        preferences.ReleaseFirstSave.TrySetResult(true);
        await Task.WhenAll(refresh, toggle);

        Assert.NotNull(preferences.Value.Baseline);
        Assert.Contains("adopted", preferences.Value.AdoptedTurnIds);
        Assert.Equal(false, preferences.Value.LaunchAtLoginEnabled);
        Assert.False(startup.IsEnabled);
        Assert.Contains("adopted", monitor.LastScanOptions!.AdoptedTurnIds);
    }

    [Fact]
    public async Task Refresh_BlockedBaselineSaveThenWindowPosition_RebasesOnTheCommittedBaseline()
    {
        var monitor = new FakeTaskMonitor(Set("adopted"), []);
        var preferences = new CommitBlockingPreferencesStore(MonitorPreferences.Empty);
        var viewModel = Create(monitor, preferences);

        var refresh = viewModel.RefreshAsync(CancellationToken.None);
        await preferences.FirstSaveStarted.Task;
        var position = viewModel.SaveWindowPositionAsync(12, 34, CancellationToken.None);
        preferences.ReleaseFirstSave.TrySetResult(true);
        await Task.WhenAll(refresh, position);

        Assert.NotNull(preferences.Value.Baseline);
        Assert.Equal(12, preferences.Value.WindowLeft);
        Assert.Equal(34, preferences.Value.WindowTop);
        Assert.Equal(12, viewModel.SavedWindowLeft);
        Assert.Equal(34, viewModel.SavedWindowTop);
        Assert.Contains("adopted", monitor.LastScanOptions!.AdoptedTurnIds);
    }

    [Fact]
    public async Task PreferenceMutations_DismissAndWindowPositionRemainCombinedAcrossQueuedSaves()
    {
        var item = new MonitorItem("thread", "turn", "Task", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Waiting);
        var initial = new MonitorPreferences(DateTimeOffset.UtcNow, [], [], [], null, null, true);
        var preferences = new CommitBlockingPreferencesStore(initial);
        var viewModel = Create(new FakeTaskMonitor(Set(), [item]), preferences);
        await viewModel.StartAsync(false, CancellationToken.None);

        var position = viewModel.SaveWindowPositionAsync(12, 34, CancellationToken.None);
        await preferences.FirstSaveStarted.Task;
        var dismiss = viewModel.DismissAsync(viewModel.Items.Single(), CancellationToken.None);
        preferences.ReleaseFirstSave.TrySetResult(true);
        await Task.WhenAll(position, dismiss);

        Assert.Contains("thread:turn", preferences.Value.DismissedItemIds);
        Assert.Equal(12, preferences.Value.WindowLeft);
        Assert.Equal(34, preferences.Value.WindowTop);
    }

    [Fact]
    public async Task Refresh_NewGenerationBeforeItemCommit_DoesNotPublishStaleItems()
    {
        var initial = new MonitorItem("thread", "initial", "Initial", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Running);
        var latest = new MonitorItem("thread", "latest", "Latest", @"C:\work", "work", DateTimeOffset.UtcNow, TaskState.Waiting);
        var firstScan = new TaskCompletionSource<MonitorScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new FakeTaskMonitor(Set(), []) { FirstScan = firstScan, NextScanResult = new MonitorScanResult([latest], 0) };
        var preferences = new FakePreferencesStore(new(DateTimeOffset.UtcNow, [], [], [], null, null, true));
        Task? newerRefresh = null;
        MonitorViewModel? viewModel = null;
        var hook = new CommitHook(point =>
        {
            if (point == RefreshCommitPoint.Items)
                newerRefresh ??= viewModel!.RefreshAsync(CancellationToken.None);
        });
        viewModel = Create(monitor, preferences, commitHook: hook);

        var oldRefresh = viewModel.RefreshAsync(CancellationToken.None);
        await monitor.FirstScanStarted.Task;
        firstScan.TrySetResult(new MonitorScanResult([initial], 0));
        await oldRefresh;
        await newerRefresh!;

        Assert.Collection(viewModel.Items, item => Assert.Equal(latest.Id, item.Id));
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Refresh_NewGenerationBeforeErrorCommit_DoesNotPublishStaleError()
    {
        var firstScan = new TaskCompletionSource<MonitorScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new FakeTaskMonitor(Set(), []) { FirstScan = firstScan };
        var preferences = new FakePreferencesStore(new(DateTimeOffset.UtcNow, [], [], [], null, null, true));
        Task? newerRefresh = null;
        MonitorViewModel? viewModel = null;
        var hook = new CommitHook(point =>
        {
            if (point == RefreshCommitPoint.Error)
                newerRefresh ??= viewModel!.RefreshAsync(CancellationToken.None);
        });
        viewModel = Create(monitor, preferences, commitHook: hook);

        var oldRefresh = viewModel.RefreshAsync(CancellationToken.None);
        await monitor.FirstScanStarted.Task;
        firstScan.TrySetException(new CodexDataException(CodexDataError.DatabaseMissing, "missing"));
        await oldRefresh;
        await newerRefresh!;

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(TimeSpan.FromSeconds(2), viewModel.NextPollDelay);
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
        DateTimeOffset? launchTime = null,
        IMonitorViewModelCommitHook? commitHook = null) =>
        new(monitor, preferences, new FakeActivation(), startup ?? new FakeStartup(), new FakeLaunchTime(launchTime), TimeProvider.System, commitHook);

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private sealed class FakeTaskMonitor(
        IReadOnlySet<string> adopted,
        IReadOnlyList<MonitorItem> items) : ITaskMonitor
    {
        public Exception? ScanException { get; init; }

        public TaskCompletionSource<MonitorScanResult>? FirstScan { get; set; }

        public bool FirstScanHonorsCancellation { get; init; }

        public TaskCompletionSource<IReadOnlySet<string>>? FirstAdoption { get; set; }

        public IReadOnlySet<string>? NextAdoption { get; init; }

        public TaskCompletionSource<bool> FirstAdoptionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> FirstScanStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> FirstScanCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MonitorScanResult? NextScanResult { get; set; }

        public MonitorScanOptions? LastScanOptions { get; private set; }

        public async Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken token)
        {
            if (FirstAdoption is { } firstAdoption)
            {
                FirstAdoption = null;
                FirstAdoptionStarted.TrySetResult(true);
                return await firstAdoption.Task;
            }

            return NextAdoption ?? adopted;
        }

        public async Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken token)
        {
            LastScanOptions = options;
            if (FirstScan is { } firstScan)
            {
                FirstScan = null;
                FirstScanStarted.TrySetResult(true);
                using var registration = token.Register(() => FirstScanCancelled.TrySetResult(true));
                return FirstScanHonorsCancellation
                    ? await firstScan.Task.WaitAsync(token)
                    : await firstScan.Task;
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
        public Exception? Exception { get; init; }

        public MonitorItem? LastItem { get; private set; }

        public Task<string?> ActivateAsync(MonitorItem item, CancellationToken token)
        {
            LastItem = item;
            return Exception is null
                ? Task.FromResult<string?>(null)
                : Task.FromException<string?>(Exception);
        }
    }

    private sealed class BlockingPreferencesStore(MonitorPreferences initial) : IMonitorPreferencesStore
    {
        private bool firstSave = true;

        public MonitorPreferences Value { get; private set; } = initial;

        public TaskCompletionSource<bool> FirstSaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MonitorPreferences> LoadAsync(CancellationToken token) => Task.FromResult(Value);

        public async Task SaveAsync(MonitorPreferences value, CancellationToken token)
        {
            if (firstSave)
            {
                firstSave = false;
                FirstSaveStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            Value = value;
        }
    }

    private sealed class CommitBlockingPreferencesStore(MonitorPreferences initial) : IMonitorPreferencesStore
    {
        private bool firstSave = true;

        public MonitorPreferences Value { get; private set; } = initial;

        public TaskCompletionSource<bool> FirstSaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseFirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MonitorPreferences> LoadAsync(CancellationToken token) => Task.FromResult(Value);

        public async Task SaveAsync(MonitorPreferences value, CancellationToken token)
        {
            if (firstSave)
            {
                firstSave = false;
                FirstSaveStarted.TrySetResult(true);
                await ReleaseFirstSave.Task;
            }

            Value = value;
        }
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

    private sealed class CommitHook(Action<RefreshCommitPoint> action) : IMonitorViewModelCommitHook
    {
        public void BeforeCommit(RefreshCommitPoint point) => action(point);
    }
}
