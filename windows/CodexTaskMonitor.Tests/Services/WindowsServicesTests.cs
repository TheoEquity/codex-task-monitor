using CodexTaskMonitor.Windows.Services;

namespace CodexTaskMonitor.Tests.Services;

public sealed class WindowsServicesTests
{
    [Fact]
    public void StartupRegistration_QuotesExecutableAndDeletesSameValue()
    {
        var values = new FakeRunValueStore();
        var registration = new StartupRegistration(values, @"C:\Apps\Codex Task Monitor\CodexTaskMonitor.exe");

        registration.SetEnabled(true);

        Assert.Equal("\"C:\\Apps\\Codex Task Monitor\\CodexTaskMonitor.exe\"", values.Value);
        Assert.True(registration.IsEnabled);

        registration.SetEnabled(false);

        Assert.Null(values.Value);
    }

    [Fact]
    public void SingleInstance_SecondOwnerWithSameNameIsRejected()
    {
        var name = $"CodexTaskMonitor.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceService.TryAcquire(name);
        using var second = SingleInstanceService.TryAcquire(name);

        Assert.True(first.IsOwner);
        Assert.False(second.IsOwner);
    }

    [Fact]
    public async Task SingleInstance_SecondLaunchSignalsExistingOwner()
    {
        var name = $"CodexTaskMonitor.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceService.TryAcquire(name);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ActivationRequested += (_, _) => activated.TrySetResult();

        using var second = SingleInstanceService.TryAcquire(name);

        Assert.False(second.IsOwner);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SingleInstance_DeliversActivationThatArrivedBeforeSubscriptionAndContinuesSignaling()
    {
        var name = $"CodexTaskMonitor.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceService.TryAcquire(name);
        using var second = SingleInstanceService.TryAcquire(name);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var firstActivation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondActivation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activationCount = 0;
        first.ActivationRequested += (_, _) =>
        {
            var count = Interlocked.Increment(ref activationCount);
            if (count == 1)
                firstActivation.TrySetResult();
            if (count == 2)
                secondActivation.TrySetResult();
        };

        await firstActivation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, Volatile.Read(ref activationCount));

        using var third = SingleInstanceService.TryAcquire(name);

        await secondActivation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Volatile.Read(ref activationCount));
    }

    [Fact]
    public async Task SingleInstance_ActivationHandlerCanDisposeOwner()
    {
        var name = $"CodexTaskMonitor.Tests.{Guid.NewGuid():N}";
        var first = SingleInstanceService.TryAcquire(name);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ActivationRequested += (_, _) =>
        {
            first.Dispose();
            disposed.TrySetResult();
        };

        using var second = SingleInstanceService.TryAcquire(name);

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SingleInstance_ObjectNameUsesGlobalUserScope()
    {
        var name = SingleInstanceService.BuildObjectName("CodexTaskMonitor.Tests", "S-1-5-21-123");

        Assert.Equal(@"Global\CodexTaskMonitor.Tests.S-1-5-21-123", name);
    }

    [Fact]
    public void SingleInstance_ReleasesMutexWhenOwnerIsDisposed()
    {
        var name = $"CodexTaskMonitor.Tests.{Guid.NewGuid():N}";
        using (var first = SingleInstanceService.TryAcquire(name))
        {
            Assert.True(first.IsOwner);
        }

        using var next = SingleInstanceService.TryAcquire(name);

        Assert.True(next.IsOwner);
    }

    [Fact]
    public async Task SingleInstance_ReleasesMutexWhenDisposedFromAnotherThread()
    {
        var name = $"CodexTaskMonitor.Tests.{Guid.NewGuid():N}";
        var first = SingleInstanceService.TryAcquire(name);
        Assert.True(first.IsOwner);

        await Task.Run(first.Dispose);

        using var next = SingleInstanceService.TryAcquire(name);
        Assert.True(next.IsOwner);
    }

    [Fact]
    public async Task Diagnostics_DoesNotAcceptTaskContentAndRotatesBySize()
    {
        var root = Path.Combine(Path.GetTempPath(), $"logs-{Guid.NewGuid():N}");
        try
        {
            var log = new LocalDiagnostics(root, maxBytes: 120, retainedFiles: 2);
            for (var index = 0; index < 20; index++)
                await log.WriteAsync("uia-timeout", TimeSpan.FromMilliseconds(20), 1, default);
            await log.WriteAsync("title=private; prompt=private; rollout=private; credential=private; auth=private; thread-id=private; C:\\Users\\private", TimeSpan.Zero, 1, default);

            var files = Directory.GetFiles(root);
            var contents = string.Concat(await Task.WhenAll(files.Select(file => File.ReadAllTextAsync(file))));
            Assert.InRange(files.Length, 1, 2);
            Assert.DoesNotContain("title", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("prompt", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rollout", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("credential", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("auth", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("thread-id", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("C:\\Users", contents, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Diagnostics_RetainedSingleFileDoesNotGrowWithoutBound()
    {
        var root = Path.Combine(Path.GetTempPath(), $"logs-{Guid.NewGuid():N}");
        try
        {
            var log = new LocalDiagnostics(root, maxBytes: 300, retainedFiles: 1);
            for (var index = 0; index < 20; index++)
                await log.WriteAsync("uia-timeout", TimeSpan.FromMilliseconds(20), 1, default);

            var file = new FileInfo(Path.Combine(root, "monitor.log"));
            Assert.InRange(file.Length, 1, 350);
            Assert.Single(Directory.GetFiles(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeRunValueStore : IRunValueStore
    {
        public string? Value { get; private set; }

        public string? Read() => Value;

        public void Write(string value) => Value = value;

        public void Delete() => Value = null;
    }
}
