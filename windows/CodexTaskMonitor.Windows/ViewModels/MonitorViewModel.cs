using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexTaskMonitor.Core;
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;

namespace CodexTaskMonitor.Windows.ViewModels;

public interface IThreadActivationService { Task<string?> ActivateAsync(MonitorItem item, CancellationToken token); }
public interface IStartupRegistration { bool IsEnabled { get; } void SetEnabled(bool enabled); }
public interface ICodexLaunchTimeProvider { DateTimeOffset? GetLaunchTime(); }

internal enum RefreshCommitPoint { Baseline, Items, Error }
internal interface IMonitorViewModelCommitHook { void BeforeCommit(RefreshCommitPoint point); }

public sealed class MonitorViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan NormalPollDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MissingDatabasePollDelay = TimeSpan.FromSeconds(10);
    private const string ActionFailureMessage = "暂时无法完成此操作，请重试";

    private readonly ITaskMonitor monitor;
    private readonly IMonitorPreferencesStore preferenceStore;
    private readonly IThreadActivationService activation;
    private readonly IStartupRegistration startup;
    private readonly ICodexLaunchTimeProvider launchTime;
    private readonly TimeProvider time;
    private readonly IMonitorViewModelCommitHook? commitHook;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly SemaphoreSlim preferenceMutationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object refreshSync = new();
    private readonly DateTimeOffset firstAttemptBaseline;
    private readonly HashSet<Task> activeRefreshes = [];
    private MonitorPreferences preferences = MonitorPreferences.Empty;
    private bool preferencesLoaded;
    private CancellationTokenSource? activeRefresh;
    private Task? polling;
    private Task? disposal;
    private string? actionErrorMessage;
    private string? scanErrorMessage;
    private long refreshGeneration;
    private bool disposing;
    private TimeSpan nextPollDelay = NormalPollDelay;

    public MonitorViewModel(
        ITaskMonitor monitor,
        IMonitorPreferencesStore preferenceStore,
        IThreadActivationService activation,
        IStartupRegistration startup,
        ICodexLaunchTimeProvider launchTime,
        TimeProvider time)
        : this(monitor, preferenceStore, activation, startup, launchTime, time, null)
    {
    }

    internal MonitorViewModel(
        ITaskMonitor monitor,
        IMonitorPreferencesStore preferenceStore,
        IThreadActivationService activation,
        IStartupRegistration startup,
        ICodexLaunchTimeProvider launchTime,
        TimeProvider time,
        IMonitorViewModelCommitHook? commitHook)
    {
        this.monitor = monitor;
        this.preferenceStore = preferenceStore;
        this.activation = activation;
        this.startup = startup;
        this.launchTime = launchTime;
        this.time = time;
        this.commitHook = commitHook;
        firstAttemptBaseline = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        RefreshCommand = new AsyncCommand(() => RefreshAsync(lifetime.Token), onError: ReportActionFailure);
        ToggleStartupCommand = new AsyncCommand(() => ToggleStartupAsync(lifetime.Token), onError: ReportActionFailure);
        QuitCommand = new AsyncCommand(() => { QuitRequested?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }, onError: ReportActionFailure);
    }

    public ObservableCollection<MonitorItemViewModel> Items { get; } = [];
    public string? ErrorMessage => actionErrorMessage ?? scanErrorMessage;
    public bool HasError => ErrorMessage is not null;
    public double PanelHeight => MonitorPanelLayout.Height(Items.Count, HasError);
    public double? SavedWindowLeft => preferences.WindowLeft;
    public double? SavedWindowTop => preferences.WindowTop;
    public bool IsStartupEnabled => preferences.LaunchAtLoginEnabled ?? startup.IsEnabled;
    internal TimeSpan NextPollDelay => nextPollDelay;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ToggleStartupCommand { get; }
    public AsyncCommand QuitCommand { get; }
    public event EventHandler? QuitRequested;
    public event EventHandler<string>? ItemInserted;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task StartAsync(bool startPollingLoop, CancellationToken token)
    {
        var initialStartupSetting = startup.IsEnabled;
        await MutatePreferencesAsync(
            current => current.LaunchAtLoginEnabled is null
                ? current.WithLaunchAtLogin(initialStartupSetting)
                : current,
            token,
            committed => startup.SetEnabled(committed.LaunchAtLoginEnabled ?? initialStartupSetting));

        OnPropertyChanged(nameof(IsStartupEnabled));
        OnPropertyChanged(nameof(SavedWindowLeft));
        OnPropertyChanged(nameof(SavedWindowTop));
        await RefreshAsync(token);
        if (startPollingLoop)
            polling ??= PollAsync(lifetime.Token);
    }

    public async Task ToggleStartupAsync(CancellationToken token)
    {
        await MutatePreferencesAsync(
            current => current.WithLaunchAtLogin(!(current.LaunchAtLoginEnabled ?? startup.IsEnabled)),
            token,
            committed => startup.SetEnabled(committed.LaunchAtLoginEnabled ?? startup.IsEnabled));
        OnPropertyChanged(nameof(IsStartupEnabled));
    }

    public async Task OpenAsync(MonitorItemViewModel item, CancellationToken token)
    {
        SetActionError(null);
        try { SetActionError(await activation.ActivateAsync(item.Item, token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { ReportActionFailure(); }
    }

    public async Task DismissAsync(MonitorItemViewModel item, CancellationToken token)
    {
        if (!item.CanDismiss)
            return;

        try
        {
            await MutatePreferencesAsync(current => current.Dismiss(item.Id), token);
            await RefreshAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { ReportActionFailure(); }
    }

    public async Task SaveWindowPositionAsync(double left, double top, CancellationToken token)
    {
        try
        {
            await MutatePreferencesAsync(current => current.WithWindowPosition(left, top), token);
            OnPropertyChanged(nameof(SavedWindowLeft));
            OnPropertyChanged(nameof(SavedWindowTop));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { ReportActionFailure(); }
    }

    public Task RefreshAsync(CancellationToken token)
    {
        RefreshScope refresh;
        TaskCompletionSource completion;
        lock (refreshSync)
        {
            if (disposing)
                return Task.FromCanceled(new CancellationToken(canceled: true));

            activeRefresh?.Cancel();
            activeRefresh = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            refresh = new RefreshScope(++refreshGeneration, activeRefresh);
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            activeRefreshes.Add(completion.Task);
        }

        _ = RunRefreshAsync(refresh, token, completion);
        return completion.Task;
    }

    public void ReportActionFailure() => SetActionError(ActionFailureMessage);

    private async Task RunRefreshAsync(RefreshScope refresh, CancellationToken callerToken, TaskCompletionSource completion)
    {
        var enteredGate = false;
        Exception? failure = null;
        try
        {
            await refreshGate.WaitAsync(refresh.Token);
            enteredGate = true;
            await RefreshCoreAsync(refresh, refresh.Token);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && !lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            if (enteredGate)
                refreshGate.Release();
            EndRefresh(refresh, completion.Task);
            if (failure is null)
                completion.TrySetResult();
            else
                completion.TrySetException(failure);
        }
    }

    private async Task RefreshCoreAsync(RefreshScope refresh, CancellationToken token)
    {
        try
        {
            await EnsurePreferencesLoadedAsync(token);
            if (preferences.Baseline is null)
            {
                var hourAgo = firstAttemptBaseline.AddHours(-1);
                var activeSince = launchTime.GetLaunchTime() is { } launched && launched > hourAgo ? launched : hourAgo;
                var adopted = await Task.Run(
                    () => monitor.CurrentlyRunningTurnIdsAsync(activeSince, token),
                    token);
                if (!TryCommit(refresh, RefreshCommitPoint.Baseline, () => { }))
                    return;

                await MutatePreferencesAsync(
                    current => current.Baseline is null
                        ? current.Initialize(firstAttemptBaseline, adopted)
                        : current,
                    token);

                if (!TryCommit(refresh, RefreshCommitPoint.Baseline, () => { }))
                    return;
            }

            var scanOptions = new MonitorScanOptions(
                preferences.Baseline!.Value,
                preferences.AdoptedTurnIds,
                preferences.DismissedTurnIds,
                preferences.DismissedItemIds);
            var result = await Task.Run(() => monitor.ScanAsync(scanOptions, token), token);
            string? insertedId = null;
            var applied = TryCommit(refresh, RefreshCommitPoint.Items, () =>
            {
                insertedId = UpdateItems(result.Items);
                nextPollDelay = NormalPollDelay;
                SetScanError(result.UnreadableRolloutCount == 0 ? null : $"{result.UnreadableRolloutCount} 个任务暂时无法读取");
            });
            if (applied && insertedId is not null)
                ItemInserted?.Invoke(this, insertedId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            TryCommit(refresh, RefreshCommitPoint.Error, () =>
            {
                nextPollDelay = error is CodexDataException { Error: CodexDataError.DatabaseMissing }
                    ? MissingDatabasePollDelay
                    : NormalPollDelay;
                SetScanError(UserMessage(error));
            });
        }
    }

    private bool TryCommit(RefreshScope refresh, RefreshCommitPoint point, Action commit)
    {
        commitHook?.BeforeCommit(point);
        lock (refreshSync)
        {
            if (disposing || refresh.Generation != refreshGeneration)
                return false;

            commit();
            return true;
        }
    }

    private string? UpdateItems(IReadOnlyList<MonitorItem> items)
    {
        var oldIds = Items.Select(item => item.Id).ToArray();
        var nextItems = items.Select(item => new MonitorItemViewModel(item)).ToArray();
        var insertedId = MonitorListUpdate.InsertedId(oldIds, nextItems.Select(item => item.Id).ToArray());
        if (Items.SequenceEqual(nextItems))
            return null;

        Items.Clear();
        foreach (var item in nextItems)
            Items.Add(item);
        OnPropertyChanged(nameof(PanelHeight));
        return insertedId;
    }

    private async Task EnsurePreferencesLoadedAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await preferenceMutationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!preferencesLoaded)
            {
                preferences = await preferenceStore.LoadAsync(linked.Token).ConfigureAwait(false);
                preferencesLoaded = true;
            }
        }
        finally
        {
            preferenceMutationGate.Release();
        }
    }

    private async Task<MonitorPreferences> MutatePreferencesAsync(
        Func<MonitorPreferences, MonitorPreferences> mutation,
        CancellationToken token,
        Action<MonitorPreferences>? afterCommit = null)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await preferenceMutationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!preferencesLoaded)
            {
                preferences = await preferenceStore.LoadAsync(linked.Token).ConfigureAwait(false);
                preferencesLoaded = true;
            }

            var committed = mutation(preferences);
            if (!ReferenceEquals(committed, preferences))
            {
                await preferenceStore.SaveAsync(committed, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                preferences = committed;
            }

            afterCommit?.Invoke(preferences);
            return preferences;
        }
        finally
        {
            preferenceMutationGate.Release();
        }
    }

    private void EndRefresh(RefreshScope refresh, Task completion)
    {
        lock (refreshSync)
        {
            if (ReferenceEquals(activeRefresh, refresh.Source))
                activeRefresh = null;
            activeRefreshes.Remove(completion);
        }

        refresh.Source.Dispose();
    }

    private async Task PollAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(nextPollDelay, time, token);
                await RefreshAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void SetScanError(string? value)
    {
        if (scanErrorMessage == value)
            return;

        scanErrorMessage = value;
        RaiseErrorProperties();
    }

    private void SetActionError(string? value)
    {
        if (actionErrorMessage == value)
            return;

        actionErrorMessage = value;
        RaiseErrorProperties();
    }

    private void RaiseErrorProperties()
    {
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(PanelHeight));
    }

    private static string UserMessage(Exception error) => error switch
    {
        CodexDataException { Error: CodexDataError.DatabaseMissing } => "未找到本机 Codex 数据",
        CodexDataException { Error: CodexDataError.FormatChanged } => "Codex 数据格式已变化",
        _ => "暂时无法读取 Codex 数据"
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ValueTask DisposeAsync()
    {
        Task? poll;
        IReadOnlyCollection<Task> refreshes;
        TaskCompletionSource completion;
        lock (refreshSync)
        {
            if (disposal is not null)
                return new ValueTask(disposal);

            disposing = true;
            lifetime.Cancel();
            activeRefresh?.Cancel();
            poll = polling;
            refreshes = activeRefreshes.ToArray();
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposal = completion.Task;
        }

        _ = DisposeCoreAsync(poll, refreshes, completion);
        return new ValueTask(disposal);
    }

    private async Task DisposeCoreAsync(
        Task? poll,
        IReadOnlyCollection<Task> refreshes,
        TaskCompletionSource completion)
    {
        try
        {
            await AwaitWithoutFailureAsync(poll);
            await AwaitWithoutFailureAsync(Task.WhenAll(refreshes));
            await preferenceMutationGate.WaitAsync().ConfigureAwait(false);
            preferenceMutationGate.Release();
            lifetime.Dispose();
            refreshGate.Dispose();
            preferenceMutationGate.Dispose();
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private static async Task AwaitWithoutFailureAsync(Task? task)
    {
        if (task is null)
            return;
        try { await task; }
        catch { }
    }

    private sealed record RefreshScope(long Generation, CancellationTokenSource Source)
    {
        public CancellationToken Token => Source.Token;
    }
}
