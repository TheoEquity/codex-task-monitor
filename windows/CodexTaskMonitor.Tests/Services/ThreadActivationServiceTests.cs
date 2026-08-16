using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Windows.Automation;
using CodexTaskMonitor.Windows.Services;

namespace CodexTaskMonitor.Tests.Services;

public sealed class ThreadActivationServiceTests
{
    [Fact]
    public async Task Activate_OpensDeepLinkBeforeSidebarReveal()
    {
        var calls = new List<string>();
        var service = new ThreadActivationService(
            new FakeDeepLink(true, calls), new FakeRevealer(null, calls), new FakeDiagnostics(), TimeProvider.System);

        var error = await service.ActivateAsync(Item(), CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(["open", "reveal"], calls);
    }

    [Fact]
    public async Task DeepLinkFailure_SkipsSidebarReveal()
    {
        var calls = new List<string>();
        var service = new ThreadActivationService(
            new FakeDeepLink(false, calls), new FakeRevealer(null, calls), new FakeDiagnostics(), TimeProvider.System);

        Assert.Equal("无法打开对应的 Codex 对话", await service.ActivateAsync(Item(), CancellationToken.None));
        Assert.Equal(["open"], calls);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForPendingReveal()
    {
        var revealer = new BlockingRevealer();
        var service = new ThreadActivationService(
            new FakeDeepLink(true, []), revealer, new FakeDiagnostics(), TimeProvider.System);

        var activation = service.ActivateAsync(Item(), CancellationToken.None);
        await revealer.Started.Task;
        var disposal = service.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        revealer.Complete.TrySetResult(null);
        await disposal;
        await activation;
    }

    [Fact]
    public async Task DeepLinkFailure_RemainsSafeWhenDiagnosticsCannotWrite()
    {
        var service = new ThreadActivationService(
            new FakeDeepLink(false, []), new FakeRevealer(null, []), new ThrowingDiagnostics(), TimeProvider.System);

        var error = await service.ActivateAsync(Item(), CancellationToken.None);

        Assert.Equal("无法打开对应的 Codex 对话", error);
    }

    private static MonitorItem Item() => new(
        "11111111-1111-4111-8111-111111111111", "turn", "Task", @"C:\work", "work",
        DateTimeOffset.UtcNow, TaskState.Waiting);

    private sealed class FakeDeepLink(bool succeeds, List<string> calls) : ICodexDeepLinkLauncher
    {
        public bool Open(string threadId) { calls.Add("open"); return succeeds; }
    }

    private sealed class FakeRevealer(string? result, List<string> calls) : IWindowsSidebarRevealer
    {
        public Task<string?> RevealAsync(MonitorItem item, CancellationToken token)
        {
            calls.Add("reveal");
            return Task.FromResult(result);
        }
    }

    private sealed class FakeDiagnostics : ILocalDiagnostics
    {
        public List<string> Categories { get; } = [];

        public Task WriteAsync(string category, TimeSpan duration, int count, CancellationToken token)
        {
            Categories.Add(category);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingRevealer : IWindowsSidebarRevealer
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<string?> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> RevealAsync(MonitorItem item, CancellationToken token)
        {
            Started.TrySetResult(true);
            return Complete.Task;
        }
    }

    private sealed class ThrowingDiagnostics : ILocalDiagnostics
    {
        public Task WriteAsync(string category, TimeSpan duration, int count, CancellationToken token) =>
            Task.FromException(new IOException("The diagnostics path is unavailable."));
    }
}
