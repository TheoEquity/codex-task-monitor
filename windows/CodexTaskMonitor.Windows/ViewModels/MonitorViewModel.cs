using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexTaskMonitor.Core;
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;

namespace CodexTaskMonitor.Windows.ViewModels;

public interface IThreadActivationService
{
    Task<string?> ActivateAsync(MonitorItem item, CancellationToken token);
}

public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

public interface ICodexLaunchTimeProvider
{
    DateTimeOffset? GetLaunchTime();
}

public sealed class MonitorViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan NormalPollDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MissingDatabasePollDelay = TimeSpan.FromSeconds(10);

    private readonly ITaskMonitor monitor;
    private readonly IMonitorPreferencesStore preferenceStore;
    private readonly IThreadActivationService activation;
    private readonly IStartupRegistration startup;
    private readonly ICodexLaunchTimeProvider launchTime;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object refreshSync = new();
    private readonly DateTimeOffset firstAttemptBaseline;
    private MonitorPreferences preferences = MonitorPreferences.Empty;
    private CancellationTokenSource? activeRefresh;
    private string? errorMessage;
    private Task? polling;
    private long refreshGeneration;
    private TimeSpan nextPollDelay = NormalPollDelay;

    public MonitorViewModel(
        ITaskMonitor monitor,
        IMonitorPreferencesStore preferenceStore,
        IThreadActivationService activation,
        IStartupRegistration startup,
        ICodexLaunchTimeProvider launchTime,
        TimeProvider time)
    {
        this.monitor = monitor;
        this.preferenceStore = preferenceStore;
        this.activation = activation;
        this.startup = startup;
        this.launchTime = launchTime;
        this.time = time;
        firstAttemptBaseline = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        RefreshCommand = new AsyncCommand(() => RefreshAsync(lifetime.Token));
        ToggleStartupCommand = new AsyncCommand(() => ToggleStartupAsync(lifetime.Token));
        QuitCommand = new AsyncCommand(() =>
        {
            QuitRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
    }

    public ObservableCollection<MonitorItemViewModel> Items { get; } = [];

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (errorMessage == value)
                return;

            errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(PanelHeight));
        }
    }

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
        preferences = await preferenceStore.LoadAsync(token);
        var launchAtLogin = preferences.LaunchAtLoginEnabled ?? startup.IsEnabled;
        startup.SetEnabled(launchAtLogin);
        if (preferences.LaunchAtLoginEnabled is null)
        {
            preferences = preferences.WithLaunchAtLogin(launchAtLogin);
            await preferenceStore.SaveAsync(preferences, token);
        }

        OnPropertyChanged(nameof(IsStartupEnabled));
        OnPropertyChanged(nameof(SavedWindowLeft));
        OnPropertyChanged(nameof(SavedWindowTop));
        await RefreshAsync(token);
        if (startPollingLoop)
            polling ??= PollAsync(lifetime.Token);
    }

    public async Task ToggleStartupAsync(CancellationToken token)
    {
        var enabled = !IsStartupEnabled;
        startup.SetEnabled(enabled);
        preferences = preferences.WithLaunchAtLogin(enabled);
        await preferenceStore.SaveAsync(preferences, token);
        OnPropertyChanged(nameof(IsStartupEnabled));
    }

    public async Task OpenAsync(MonitorItemViewModel item, CancellationToken token) =>
        ErrorMessage = await activation.ActivateAsync(item.Item, token);

    public async Task DismissAsync(MonitorItemViewModel item, CancellationToken token)
    {
        if (!item.CanDismiss)
            return;

        preferences = preferences.Dismiss(item.Id);
        await preferenceStore.SaveAsync(preferences, token);
        await RefreshAsync(token);
    }

    public async Task SaveWindowPositionAsync(double left, double top, CancellationToken token)
    {
        preferences = preferences.WithWindowPosition(left, top);
        await preferenceStore.SaveAsync(preferences, token);
        OnPropertyChanged(nameof(SavedWindowLeft));
        OnPropertyChanged(nameof(SavedWindowTop));
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        var refresh = BeginRefresh(token);
        try
        {
            await refreshGate.WaitAsync(refresh.Token);
            try
            {
                await RefreshCurrentGenerationAsync(refresh.Generation, refresh.Token);
            }
            finally
            {
                refreshGate.Release();
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !lifetime.IsCancellationRequested)
        {
            // A newer refresh superseded this generation; it owns any UI updates.
        }
        finally
        {
            EndRefresh(refresh);
        }
    }

    private async Task RefreshCoreAsync(long generation, CancellationToken token)
    {
        if (preferences.Baseline is null)
        {
            var hourAgo = firstAttemptBaseline.AddHours(-1);
            var activeSince = launchTime.GetLaunchTime() is { } launched && launched > hourAgo ? launched : hourAgo;
            var adopted = await monitor.CurrentlyRunningTurnIdsAsync(activeSince, token);
            if (!IsCurrentGeneration(generation))
                return;

            preferences = preferences.Initialize(firstAttemptBaseline, adopted);
            await preferenceStore.SaveAsync(preferences, token);
        }

        var scanOptions = new MonitorScanOptions(
            preferences.Baseline!.Value,
            preferences.AdoptedTurnIds,
            preferences.DismissedTurnIds,
            preferences.DismissedItemIds);
        var result = await monitor.ScanAsync(scanOptions, token);
        if (!IsCurrentGeneration(generation))
            return;

        UpdateItems(result.Items);
        nextPollDelay = NormalPollDelay;
        ErrorMessage = result.UnreadableRolloutCount == 0
            ? null
            : $"{result.UnreadableRolloutCount} 个任务暂时无法读取";
        OnPropertyChanged(nameof(PanelHeight));
    }

    private void UpdateItems(IReadOnlyList<MonitorItem> items)
    {
        var oldIds = Items.Select(item => item.Id).ToArray();
        var nextItems = items.Select(item => new MonitorItemViewModel(item)).ToArray();
        var insertedId = MonitorListUpdate.InsertedId(oldIds, nextItems.Select(item => item.Id).ToArray());
        if (Items.SequenceEqual(nextItems))
            return;

        Items.Clear();
        foreach (var item in nextItems)
            Items.Add(item);

        if (insertedId is not null)
            ItemInserted?.Invoke(this, insertedId);
        OnPropertyChanged(nameof(PanelHeight));
    }

    private RefreshScope BeginRefresh(CancellationToken token)
    {
        lock (refreshSync)
        {
            activeRefresh?.Cancel();
            activeRefresh = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            return new RefreshScope(++refreshGeneration, activeRefresh);
        }
    }

    private void EndRefresh(RefreshScope refresh)
    {
        lock (refreshSync)
        {
            if (ReferenceEquals(activeRefresh, refresh.Source))
                activeRefresh = null;
        }

        refresh.Source.Dispose();
    }

    private bool IsCurrentGeneration(long generation) => Volatile.Read(ref refreshGeneration) == generation;

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

    private static string UserMessage(Exception error) => error switch
    {
        CodexDataException { Error: CodexDataError.DatabaseMissing } => "未找到本机 Codex 数据",
        CodexDataException { Error: CodexDataError.FormatChanged } => "Codex 数据格式已变化",
        _ => "暂时无法读取 Codex 数据"
    };

    private async Task RefreshCurrentGenerationAsync(long generation, CancellationToken token)
    {
        try
        {
            await RefreshCoreAsync(generation, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            if (!IsCurrentGeneration(generation))
                return;

            nextPollDelay = error is CodexDataException { Error: CodexDataError.DatabaseMissing }
                ? MissingDatabasePollDelay
                : NormalPollDelay;
            ErrorMessage = UserMessage(error);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        lock (refreshSync)
        {
            activeRefresh?.Cancel();
        }

        if (polling is not null)
            await polling;

        lifetime.Dispose();
        refreshGate.Dispose();
    }

    private sealed record RefreshScope(long Generation, CancellationTokenSource Source)
    {
        public CancellationToken Token => Source.Token;
    }
}
