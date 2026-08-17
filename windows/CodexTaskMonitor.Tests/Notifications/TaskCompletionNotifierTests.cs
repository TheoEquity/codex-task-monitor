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

    private static MonitorItem Item(TaskTerminalKind terminalKind, DateTimeOffset eventDate) =>
        new("thread", "turn", "Task title", @"C:\Project", "Project", eventDate, TaskState.Waiting, terminalKind);

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
                initialRetryDelay: TimeSpan.FromMilliseconds(20),
                maxRetryDelay: TimeSpan.FromMilliseconds(40));
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

    private sealed class NullDiagnostics : ILocalDiagnostics
    {
        public Task WriteAsync(string category, TimeSpan duration, int count, CancellationToken token) =>
            Task.CompletedTask;
    }
}
