using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Windows.Notifications;
using CodexTaskMonitor.Windows.Services;

namespace CodexTaskMonitor.Tests.Notifications;

public sealed class TaskCompletionNotifierTests
{
    [Fact]
    public async Task CompletedItemAfterBoundary_SendsTitleAndProjectOnce()
    {
        await using var fixture = await NotifierFixture.CreateAsync();
        var item = Item(TaskTerminalKind.Completed, fixture.EnabledAt.AddSeconds(1));

        fixture.Notifier.Observe([item]);
        await fixture.Client.WaitForCallsAsync(1);
        fixture.Notifier.Observe([item]);
        await Task.Delay(50);

        var sent = Assert.Single(fixture.Client.Notifications);
        Assert.Equal("Task title", sent.Title);
        Assert.Equal("Project", sent.Body);
        Assert.Equal("Codex Task Monitor", sent.Group);
        Assert.Single((await fixture.StateStore.LoadAsync(default)).NotifiedItemIds);
        Assert.Single(fixture.Client.Notifications);
    }

    [Fact]
    public async Task AbortedItem_LabelsBodyAsAborted()
    {
        await using var fixture = await NotifierFixture.CreateAsync();

        fixture.Notifier.Observe([Item(TaskTerminalKind.Aborted, fixture.EnabledAt.AddSeconds(1))]);
        await fixture.Client.WaitForCallsAsync(1);

        Assert.Equal("Project · 已中止", fixture.Client.Notifications.Single().Body);
    }

    [Fact]
    public async Task ItemBeforeBoundary_IsNotSent()
    {
        await using var fixture = await NotifierFixture.CreateAsync();

        fixture.Notifier.Observe([Item(TaskTerminalKind.Completed, fixture.EnabledAt.AddTicks(-1))]);
        await Task.Delay(50);

        Assert.Empty(fixture.Client.Notifications);
    }

    [Fact]
    public async Task FailedSend_RetriesAndClearsWarningAfterSuccess()
    {
        await using var fixture = await NotifierFixture.CreateAsync(
            BarkSendResult.Failure(BarkSendError.Network),
            BarkSendResult.Success);
        var warnings = new List<string?>();
        fixture.Notifier.WarningChanged += (_, warning) =>
        {
            lock (warnings)
                warnings.Add(warning);
        };

        fixture.Notifier.Observe([Item(TaskTerminalKind.Completed, fixture.EnabledAt.AddSeconds(1))]);
        await fixture.Client.WaitForCallsAsync(2);
        await EventuallyAsync(async () =>
            (await fixture.StateStore.LoadAsync(default)).NotifiedItemIds.Count == 1);

        lock (warnings)
        {
            Assert.Contains("Bark 通知发送失败，将自动重试", warnings);
            Assert.Null(warnings.Last());
        }
    }

    [Fact]
    public async Task LaterSuccess_DoesNotClearWarningWhileAnotherItemIsWaitingToRetry()
    {
        await using var fixture = await NotifierFixture.CreateWithRetryDelayAsync(
            TimeSpan.FromSeconds(5),
            BarkSendResult.Failure(BarkSendError.Network),
            BarkSendResult.Success);
        var warnings = new List<string?>();
        fixture.Notifier.WarningChanged += (_, warning) =>
        {
            lock (warnings)
                warnings.Add(warning);
        };

        fixture.Notifier.Observe([
            Item(TaskTerminalKind.Completed, fixture.EnabledAt.AddSeconds(1), "thread-a", "turn-a"),
            Item(TaskTerminalKind.Completed, fixture.EnabledAt.AddSeconds(2), "thread-b", "turn-b")
        ]);
        await fixture.Client.WaitForCallsAsync(2);
        await EventuallyAsync(async () =>
            (await fixture.StateStore.LoadAsync(default)).NotifiedItemIds.Count == 1);

        lock (warnings)
            Assert.Equal("Bark 通知发送失败，将自动重试", warnings.Last());
    }

    [Fact]
    public async Task RemoveAfterFailure_CancelsRetry()
    {
        await using var fixture = await NotifierFixture.CreateAsync(
            BarkSendResult.Failure(BarkSendError.Network),
            BarkSendResult.Success);
        var item = Item(TaskTerminalKind.Completed, fixture.EnabledAt.AddSeconds(1));

        fixture.Notifier.Observe([item]);
        await fixture.Client.WaitForCallsAsync(1);
        fixture.Notifier.Remove(item.Id);
        await Task.Delay(100);

        Assert.Single(fixture.Client.Notifications);
    }

    [Fact]
    public async Task RemoveLastFailedItem_ClearsRetryWarning()
    {
        await using var fixture = await NotifierFixture.CreateWithRetryDelayAsync(
            TimeSpan.FromSeconds(5),
            BarkSendResult.Failure(BarkSendError.Network));
        var failureWarning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clearedWarning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawFailure = false;
        fixture.Notifier.WarningChanged += (_, warning) =>
        {
            if (warning == "Bark 通知发送失败，将自动重试")
            {
                sawFailure = true;
                failureWarning.TrySetResult();
            }
            else if (warning is null && sawFailure)
            {
                clearedWarning.TrySetResult();
            }
        };
        var item = Item(TaskTerminalKind.Completed, fixture.EnabledAt.AddSeconds(1));

        fixture.Notifier.Observe([item]);
        await failureWarning.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Notifier.Remove(item.Id);

        await clearedWarning.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StateFailureAfterScheduledRetry_UsesBackoffInsteadOfSpinning()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bark-notifier-{Guid.NewGuid():N}");
        try
        {
            var configurationId = Guid.NewGuid();
            var enabledAt = DateTimeOffset.UtcNow;
            var innerStore = new BarkStateStore(Path.Combine(directory, "state.json"));
            await innerStore.SaveAsync(new BarkState(true, configurationId, enabledAt, []), default);
            var stateStore = new ThrowingAfterFirstLoadStateStore(innerStore);
            var client = new SequencedClient(BarkSendResult.Failure(BarkSendError.Network));
            await using var notifier = new TaskCompletionNotifier(
                stateStore,
                new FixedSecretStore(new BarkSecret(configurationId, new Uri("https://example.invalid/device-key"))),
                client,
                new NullDiagnostics(),
                TimeProvider.System,
                initialRetryDelay: TimeSpan.FromMilliseconds(50),
                maxRetryDelay: TimeSpan.FromMilliseconds(100));

            notifier.Observe([Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(1))]);
            await client.WaitForCallsAsync(1);
            await Task.Delay(300);

            Assert.InRange(stateStore.LoadCount, 2, 6);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveOrSnapshotDisappearance_CancelsInFlightRequest(bool removeExplicitly)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bark-notifier-{Guid.NewGuid():N}");
        try
        {
            var configurationId = Guid.NewGuid();
            var enabledAt = DateTimeOffset.UtcNow;
            var stateStore = new BarkStateStore(Path.Combine(directory, "state.json"));
            await stateStore.SaveAsync(new BarkState(true, configurationId, enabledAt, []), default);
            var client = new BlockingClient();
            await using var notifier = new TaskCompletionNotifier(
                stateStore,
                new FixedSecretStore(new BarkSecret(configurationId, new Uri("https://example.invalid/device-key"))),
                client,
                new NullDiagnostics(),
                TimeProvider.System);
            var item = Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(1));

            notifier.Observe([item]);
            await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (removeExplicitly)
                notifier.Remove(item.Id);
            else
                notifier.Observe([]);

            await client.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty((await stateStore.LoadAsync(default)).NotifiedItemIds);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveOrSnapshotDisappearanceBeforeSend_PreventsRequest(bool removeExplicitly)
    {
        var configurationId = Guid.NewGuid();
        var enabledAt = DateTimeOffset.UtcNow;
        var client = new SequencedClient();
        var secretStore = new BlockingSecretStore(
            new BarkSecret(configurationId, new Uri("https://example.invalid/device-key")));
        await using var notifier = new TaskCompletionNotifier(
            new FixedStateStore(new BarkState(true, configurationId, enabledAt, [])),
            secretStore,
            client,
            new NullDiagnostics(),
            TimeProvider.System);
        var item = Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(1));

        notifier.Observe([item]);
        await secretStore.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (removeExplicitly)
            notifier.Remove(item.Id);
        else
            notifier.Observe([]);
        secretStore.Release.TrySetResult();
        await Task.Delay(50);

        Assert.Empty(client.Notifications);
    }

    [Fact]
    public async Task DisabledConfiguration_DoesNotSend()
    {
        var client = new SequencedClient();
        await using var notifier = new TaskCompletionNotifier(
            new FixedStateStore(BarkState.Disabled),
            new FixedSecretStore(new BarkSecret(Guid.NewGuid(), new Uri("https://example.invalid/device-key"))),
            client,
            new NullDiagnostics(),
            TimeProvider.System);

        notifier.Observe([Item(TaskTerminalKind.Completed, DateTimeOffset.UtcNow)]);
        await Task.Delay(50);

        Assert.Empty(client.Notifications);
    }

    [Fact]
    public async Task MismatchedSecretConfiguration_DoesNotSend()
    {
        var enabledAt = DateTimeOffset.UtcNow;
        var client = new SequencedClient();
        await using var notifier = new TaskCompletionNotifier(
            new FixedStateStore(new BarkState(true, Guid.NewGuid(), enabledAt, [])),
            new FixedSecretStore(new BarkSecret(Guid.NewGuid(), new Uri("https://example.invalid/device-key"))),
            client,
            new NullDiagnostics(),
            TimeProvider.System);

        notifier.Observe([Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(1))]);
        await Task.Delay(50);

        Assert.Empty(client.Notifications);
    }

    [Fact]
    public async Task Restart_DoesNotResendPersistedItem()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bark-notifier-{Guid.NewGuid():N}");
        try
        {
            var configurationId = Guid.NewGuid();
            var enabledAt = DateTimeOffset.UtcNow;
            var stateStore = new BarkStateStore(Path.Combine(directory, "state.json"));
            await stateStore.SaveAsync(new BarkState(true, configurationId, enabledAt, []), default);
            var secretStore = new FixedSecretStore(
                new BarkSecret(configurationId, new Uri("https://example.invalid/device-key")));
            var item = Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(1));
            var firstClient = new SequencedClient();
            await using (var first = new TaskCompletionNotifier(
                stateStore, secretStore, firstClient, new NullDiagnostics(), TimeProvider.System))
            {
                first.Observe([item]);
                await firstClient.WaitForCallsAsync(1);
                await EventuallyAsync(async () =>
                    (await stateStore.LoadAsync(default)).NotifiedItemIds.Contains(item.Id));
            }

            var secondClient = new SequencedClient();
            await using (var second = new TaskCompletionNotifier(
                stateStore, secretStore, secondClient, new NullDiagnostics(), TimeProvider.System))
            {
                second.Observe([item]);
                await Task.Delay(50);
                Assert.Empty(secondClient.Notifications);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MultipleItems_AreSentSerially()
    {
        var configurationId = Guid.NewGuid();
        var enabledAt = DateTimeOffset.UtcNow;
        var client = new SerialCheckingClient(3);
        await using var notifier = new TaskCompletionNotifier(
            new FixedStateStore(new BarkState(true, configurationId, enabledAt, [])),
            new FixedSecretStore(new BarkSecret(configurationId, new Uri("https://example.invalid/device-key"))),
            client,
            new NullDiagnostics(),
            TimeProvider.System);

        notifier.Observe([
            Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(1), "thread-a", "turn-a"),
            Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(2), "thread-b", "turn-b"),
            Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(3), "thread-c", "turn-c")
        ]);
        await client.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, client.MaximumConcurrency);
    }

    [Fact]
    public async Task Dispose_CancelsInFlightRequest()
    {
        var configurationId = Guid.NewGuid();
        var enabledAt = DateTimeOffset.UtcNow;
        var client = new BlockingClient();
        var notifier = new TaskCompletionNotifier(
            new FixedStateStore(new BarkState(true, configurationId, enabledAt, [])),
            new FixedSecretStore(new BarkSecret(configurationId, new Uri("https://example.invalid/device-key"))),
            client,
            new NullDiagnostics(),
            TimeProvider.System);

        notifier.Observe([Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(1))]);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await notifier.DisposeAsync();

        Assert.True(client.Canceled.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PersistenceFailure_DoesNotResendDuringCurrentProcess()
    {
        var configurationId = Guid.NewGuid();
        var enabledAt = DateTimeOffset.UtcNow;
        var client = new SequencedClient();
        await using var notifier = new TaskCompletionNotifier(
            new FailingMarkStateStore(new BarkState(true, configurationId, enabledAt, [])),
            new FixedSecretStore(new BarkSecret(configurationId, new Uri("https://example.invalid/device-key"))),
            client,
            new NullDiagnostics(),
            TimeProvider.System);
        var warning = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        notifier.WarningChanged += (_, value) =>
        {
            if (value == "Bark 通知状态暂时无法保存")
                warning.TrySetResult(value);
        };
        var item = Item(TaskTerminalKind.Completed, enabledAt.AddSeconds(1));

        notifier.Observe([item]);
        await client.WaitForCallsAsync(1);
        await warning.Task.WaitAsync(TimeSpan.FromSeconds(2));
        notifier.Observe([item]);
        await Task.Delay(50);

        Assert.Single(client.Notifications);
    }

    [Fact]
    public void RetryDelay_IsCappedAtConfiguredMaximum()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            TaskCompletionNotifier.CalculateRetryDelay(
                TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15), attempt: 100));
    }

    private static MonitorItem Item(
        TaskTerminalKind terminalKind,
        DateTimeOffset eventDate,
        string threadId = "thread",
        string turnId = "turn") =>
        new(threadId, turnId, "Task title", @"C:\Project", "Project", eventDate, TaskState.Waiting, terminalKind);

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!await condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class NotifierFixture : IAsyncDisposable
    {
        private readonly string directory;

        public DateTimeOffset EnabledAt { get; }
        public BarkStateStore StateStore { get; }
        public SequencedClient Client { get; }
        public TaskCompletionNotifier Notifier { get; }

        private NotifierFixture(
            string directory,
            DateTimeOffset enabledAt,
            BarkStateStore stateStore,
            SequencedClient client,
            TaskCompletionNotifier notifier)
        {
            this.directory = directory;
            EnabledAt = enabledAt;
            StateStore = stateStore;
            Client = client;
            Notifier = notifier;
        }

        public static async Task<NotifierFixture> CreateAsync(params BarkSendResult[] results)
        {
            return await CreateWithRetryDelayAsync(TimeSpan.FromMilliseconds(20), results);
        }

        public static async Task<NotifierFixture> CreateWithRetryDelayAsync(
            TimeSpan initialRetryDelay,
            params BarkSendResult[] results)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"bark-notifier-{Guid.NewGuid():N}");
            var stateStore = new BarkStateStore(Path.Combine(directory, "state.json"));
            var configurationId = Guid.NewGuid();
            var enabledAt = DateTimeOffset.UtcNow;
            await stateStore.SaveAsync(new BarkState(true, configurationId, enabledAt, []), default);
            var client = new SequencedClient(results);
            var notifier = new TaskCompletionNotifier(
                stateStore,
                new FixedSecretStore(new BarkSecret(configurationId, new Uri("https://example.invalid/device-key"))),
                client,
                new NullDiagnostics(),
                TimeProvider.System,
                initialRetryDelay: initialRetryDelay,
                maxRetryDelay: initialRetryDelay + initialRetryDelay);
            return new NotifierFixture(directory, enabledAt, stateStore, client, notifier);
        }

        public async ValueTask DisposeAsync()
        {
            await Notifier.DisposeAsync();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedSecretStore(BarkSecret secret) : IBarkSecretStore
    {
        public Task<BarkSecret?> LoadAsync(CancellationToken token) => Task.FromResult<BarkSecret?>(secret);
    }

    private sealed class BlockingSecretStore(BarkSecret secret) : IBarkSecretStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BarkSecret?> LoadAsync(CancellationToken token)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            return secret;
        }
    }

    private sealed class FixedStateStore(BarkState state) : IBarkStateStore
    {
        private BarkState current = state;

        public Task<BarkState> LoadAsync(CancellationToken token) => Task.FromResult(current);

        public Task SaveAsync(BarkState next, CancellationToken token)
        {
            current = next;
            return Task.CompletedTask;
        }

        public Task<bool> MarkNotifiedAsync(Guid expectedConfigurationId, string itemId, CancellationToken token)
        {
            if (!current.Enabled || current.ConfigurationId != expectedConfigurationId)
                return Task.FromResult(false);
            current = current.MarkNotified(itemId);
            return Task.FromResult(true);
        }
    }

    private sealed class FailingMarkStateStore(BarkState state) : IBarkStateStore
    {
        public Task<BarkState> LoadAsync(CancellationToken token) => Task.FromResult(state);
        public Task SaveAsync(BarkState next, CancellationToken token) => Task.CompletedTask;
        public Task<bool> MarkNotifiedAsync(Guid expectedConfigurationId, string itemId, CancellationToken token) =>
            Task.FromException<bool>(new IOException("state unavailable"));
    }

    private sealed class ThrowingAfterFirstLoadStateStore(IBarkStateStore inner) : IBarkStateStore
    {
        private int loadCount;

        public int LoadCount => Volatile.Read(ref loadCount);

        public Task<BarkState> LoadAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref loadCount) == 1)
                return inner.LoadAsync(token);
            return Task.FromException<BarkState>(new IOException("state unavailable"));
        }

        public Task SaveAsync(BarkState state, CancellationToken token) => inner.SaveAsync(state, token);

        public Task<bool> MarkNotifiedAsync(Guid expectedConfigurationId, string itemId, CancellationToken token) =>
            inner.MarkNotifiedAsync(expectedConfigurationId, itemId, token);
    }

    private sealed class SequencedClient(params BarkSendResult[] results) : IBarkNotificationClient
    {
        private readonly Queue<BarkSendResult> remaining = new(results);
        private readonly SemaphoreSlim calls = new(0);
        private readonly object sync = new();

        public List<BarkNotification> Notifications { get; } = [];

        public Task<BarkSendResult> SendAsync(Uri endpoint, BarkNotification notification, CancellationToken token)
        {
            BarkSendResult result;
            lock (sync)
            {
                Notifications.Add(notification);
                result = remaining.Count == 0 ? BarkSendResult.Success : remaining.Dequeue();
            }

            calls.Release();
            return Task.FromResult(result);
        }

        public async Task WaitForCallsAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            for (var index = 0; index < count; index++)
                await calls.WaitAsync(timeout.Token);
        }
    }

    private sealed class BlockingClient : IBarkNotificationClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BarkSendResult> SendAsync(
            Uri endpoint,
            BarkNotification notification,
            CancellationToken token)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return BarkSendResult.Success;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class SerialCheckingClient(int expectedCalls) : IBarkNotificationClient
    {
        private int active;
        private int completed;
        private int maximumConcurrency;

        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BarkSendResult> SendAsync(
            Uri endpoint,
            BarkNotification notification,
            CancellationToken token)
        {
            var concurrency = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maximumConcurrency, concurrency);
            try
            {
                await Task.Delay(20, token);
                if (Interlocked.Increment(ref completed) == expectedCalls)
                    Completed.TrySetResult();
                return BarkSendResult.Success;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }
    }

    private sealed class NullDiagnostics : ILocalDiagnostics
    {
        public Task WriteAsync(string category, TimeSpan duration, int count, CancellationToken token) =>
            Task.CompletedTask;
    }
}
