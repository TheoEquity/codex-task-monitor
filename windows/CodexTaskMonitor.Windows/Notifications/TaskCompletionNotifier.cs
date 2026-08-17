using System.Diagnostics;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Windows.Services;

namespace CodexTaskMonitor.Windows.Notifications;

public interface ITaskCompletionNotifier : IAsyncDisposable
{
    event EventHandler<string?>? WarningChanged;
    void Observe(IReadOnlyList<MonitorItem> items);
    void Remove(string itemId);
}

public sealed class TaskCompletionNotifier : ITaskCompletionNotifier
{
    private const string SendFailureWarning = "Bark 通知发送失败，将自动重试";
    private const string ConfigurationWarning = "Bark 配置不可用";
    private const string StateFailureWarning = "Bark 通知状态暂时无法保存";

    private readonly IBarkStateStore stateStore;
    private readonly IBarkSecretStore secretStore;
    private readonly IBarkNotificationClient client;
    private readonly ILocalDiagnostics diagnostics;
    private readonly TimeProvider time;
    private readonly TimeSpan initialRetryDelay;
    private readonly TimeSpan maxRetryDelay;
    private readonly SemaphoreSlim signal = new(0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object sync = new();
    private readonly Dictionary<string, RetryState> retries = new(StringComparer.Ordinal);
    private readonly HashSet<string> removedIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> sentInMemory = new(StringComparer.Ordinal);
    private IReadOnlyList<MonitorItem> latestSnapshot = [];
    private readonly Task worker;
    private Task? disposal;
    private Guid activeConfigurationId;
    private string? currentWarning;
    private bool disposing;

    public TaskCompletionNotifier(
        IBarkStateStore stateStore,
        IBarkSecretStore secretStore,
        IBarkNotificationClient client,
        ILocalDiagnostics diagnostics,
        TimeProvider time,
        TimeSpan? initialRetryDelay = null,
        TimeSpan? maxRetryDelay = null)
    {
        this.stateStore = stateStore;
        this.secretStore = secretStore;
        this.client = client;
        this.diagnostics = diagnostics;
        this.time = time;
        this.initialRetryDelay = initialRetryDelay ?? TimeSpan.FromSeconds(30);
        this.maxRetryDelay = maxRetryDelay ?? TimeSpan.FromMinutes(15);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.initialRetryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.maxRetryDelay, this.initialRetryDelay);
        worker = RunAsync(lifetime.Token);
    }

    public event EventHandler<string?>? WarningChanged;

    public void Observe(IReadOnlyList<MonitorItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (sync)
        {
            if (disposing)
                return;
            latestSnapshot = items.ToArray();
            signal.Release();
        }
    }

    public void Remove(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        lock (sync)
        {
            if (disposing)
                return;
            removedIds.Add(itemId);
            retries.Remove(itemId);
            latestSnapshot = latestSnapshot.Where(item => item.Id != itemId).ToArray();
            signal.Release();
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                var wait = NextWait();
                await signal.WaitAsync(wait, token).ConfigureAwait(false);
                while (signal.Wait(0))
                {
                }

                try
                {
                    await ProcessSnapshotAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    SetWarning(ConfigurationWarning);
                    await WriteDiagnosticAsync("bark-state-failure", TimeSpan.Zero, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private TimeSpan NextWait()
    {
        lock (sync)
        {
            if (retries.Count == 0)
                return Timeout.InfiniteTimeSpan;

            var next = retries.Values.Min(retry => retry.NextAttemptAt);
            var wait = next - time.GetUtcNow();
            return wait <= TimeSpan.Zero ? TimeSpan.Zero : wait;
        }
    }

    private async Task ProcessSnapshotAsync(CancellationToken token)
    {
        IReadOnlyList<MonitorItem> snapshot;
        HashSet<string> removed;
        lock (sync)
        {
            snapshot = latestSnapshot;
            removed = removedIds.ToHashSet(StringComparer.Ordinal);
        }

        var state = await stateStore.LoadAsync(token).ConfigureAwait(false);
        var secret = await secretStore.LoadAsync(token).ConfigureAwait(false);
        SwitchConfiguration(state.ConfigurationId);

        if (!state.Enabled)
        {
            ClearRetries();
            SetWarning(null);
            return;
        }

        if (state.EnabledAt is null || secret is null || secret.ConfigurationId != state.ConfigurationId)
        {
            ClearRetries();
            SetWarning(ConfigurationWarning);
            await WriteDiagnosticAsync("bark-secret-failure", TimeSpan.Zero, token).ConfigureAwait(false);
            return;
        }
        var enabledAt = state.EnabledAt.Value;

        var visibleIds = snapshot.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        lock (sync)
        {
            foreach (var stale in retries.Keys.Where(id => !visibleIds.Contains(id) || removedIds.Contains(id)).ToArray())
                retries.Remove(stale);
        }
        if (!HasPendingRetries() && currentWarning == SendFailureWarning)
            SetWarning(null);

        foreach (var item in snapshot)
        {
            token.ThrowIfCancellationRequested();
            if (item.TerminalKind is null || item.EventDate < enabledAt ||
                state.NotifiedItemIds.Contains(item.Id) || removed.Contains(item.Id) || AlreadySent(item.Id) ||
                !RetryIsDue(item.Id))
            {
                continue;
            }

            var notification = new BarkNotification(
                item.Title,
                item.TerminalKind == TaskTerminalKind.Aborted
                    ? $"{item.ProjectName} · 已中止"
                    : item.ProjectName,
                "Codex Task Monitor");
            var started = Stopwatch.GetTimestamp();
            var result = await client.SendAsync(secret.Endpoint, notification, token).ConfigureAwait(false);
            var duration = Stopwatch.GetElapsedTime(started);
            if (!result.Succeeded)
            {
                ScheduleRetry(item.Id);
                SetWarning(SendFailureWarning);
                await WriteDiagnosticAsync("bark-send-failure", duration, token).ConfigureAwait(false);
                continue;
            }

            MarkSentInMemory(item.Id);
            try
            {
                var committed = await stateStore.MarkNotifiedAsync(
                    state.ConfigurationId, item.Id, token).ConfigureAwait(false);
                if (committed)
                {
                    state = state.MarkNotified(item.Id);
                    if (!HasPendingRetries())
                        SetWarning(null);
                    await WriteDiagnosticAsync("bark-send-ok", duration, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                SetWarning(StateFailureWarning);
                await WriteDiagnosticAsync("bark-state-failure", duration, token).ConfigureAwait(false);
            }
        }
    }

    private void SwitchConfiguration(Guid configurationId)
    {
        lock (sync)
        {
            if (activeConfigurationId == configurationId)
                return;
            activeConfigurationId = configurationId;
            retries.Clear();
            sentInMemory.Clear();
        }
    }

    private bool AlreadySent(string itemId)
    {
        lock (sync)
            return sentInMemory.Contains(itemId);
    }

    private bool RetryIsDue(string itemId)
    {
        lock (sync)
            return !retries.TryGetValue(itemId, out var retry) || retry.NextAttemptAt <= time.GetUtcNow();
    }

    private void ScheduleRetry(string itemId)
    {
        lock (sync)
        {
            var attempt = retries.TryGetValue(itemId, out var retry) ? retry.Attempt + 1 : 1;
            var multiplier = Math.Pow(2, Math.Min(attempt - 1, 30));
            var ticks = Math.Min(maxRetryDelay.Ticks, initialRetryDelay.Ticks * multiplier);
            retries[itemId] = new RetryState(attempt, time.GetUtcNow().AddTicks((long)ticks));
        }
    }

    private void MarkSentInMemory(string itemId)
    {
        lock (sync)
        {
            sentInMemory.Add(itemId);
            retries.Remove(itemId);
        }
    }

    private void ClearRetries()
    {
        lock (sync)
            retries.Clear();
    }

    private bool HasPendingRetries()
    {
        lock (sync)
            return retries.Count > 0;
    }

    private void SetWarning(string? warning)
    {
        if (currentWarning == warning)
            return;
        currentWarning = warning;
        WarningChanged?.Invoke(this, warning);
    }

    private async Task WriteDiagnosticAsync(string category, TimeSpan duration, CancellationToken token)
    {
        try
        {
            await diagnostics.WriteAsync(category, duration, 1, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposal is not null)
                return new ValueTask(disposal);
            disposing = true;
            lifetime.Cancel();
            signal.Release();
            disposal = DisposeCoreAsync();
            return new ValueTask(disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await worker.ConfigureAwait(false);
        lifetime.Dispose();
        signal.Dispose();
    }

    private sealed record RetryState(int Attempt, DateTimeOffset NextAttemptAt);
}
