using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Windows.Automation;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Windows.Services;

public sealed class ThreadActivationService(
    ICodexDeepLinkLauncher links,
    IWindowsSidebarRevealer revealer,
    ILocalDiagnostics diagnostics,
    TimeProvider time) : IThreadActivationService, IAsyncDisposable
{
    private readonly object revealSync = new();
    private readonly HashSet<Task> activeOperations = [];
    private CancellationTokenSource? activeReveal;
    private Task? disposal;
    private bool disposed;

    public Task<string?> ActivateAsync(MonitorItem item, CancellationToken token)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (revealSync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activeOperations.Add(completion.Task);
        }

        _ = CompleteActivationAsync(item, token, completion);
        return completion.Task;
    }

    public ValueTask DisposeAsync()
    {
        Task completion;
        lock (revealSync)
        {
            if (disposal is not null)
                return new ValueTask(disposal);

            disposed = true;
            activeReveal?.Cancel();
            completion = AwaitOperationsAsync(activeOperations.ToArray());
            disposal = completion;
        }

        return new ValueTask(completion);
    }

    private async Task CompleteActivationAsync(
        MonitorItem item,
        CancellationToken token,
        TaskCompletionSource<string?> completion)
    {
        try
        {
            completion.TrySetResult(await ActivateCoreAsync(item, token).ConfigureAwait(false));
        }
        catch (OperationCanceledException error)
        {
            completion.TrySetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
        finally
        {
            lock (revealSync)
            {
                activeOperations.Remove(completion.Task);
            }
        }
    }

    private async Task<string?> ActivateCoreAsync(MonitorItem item, CancellationToken token)
    {
        var started = time.GetTimestamp();
        var reveal = BeginReveal(token);
        try
        {
            if (!links.Open(item.ThreadId))
            {
                await WriteDiagnosticsAsync("deep-link-failed", started).ConfigureAwait(false);
                return "无法打开对应的 Codex 对话";
            }

            try
            {
                var message = await revealer.RevealAsync(item, reveal.Token).ConfigureAwait(false);
                await WriteDiagnosticsAsync(message is null ? "reveal-ok" : "reveal-warning", started)
                    .ConfigureAwait(false);
                return message;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await WriteDiagnosticsAsync("reveal-error", started).ConfigureAwait(false);
                return "已打开对话；暂时无法在侧栏定位";
            }
        }
        finally
        {
            EndReveal(reveal);
        }
    }

    private CancellationTokenSource BeginReveal(CancellationToken token)
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(token);
        CancellationTokenSource? previous;
        lock (revealSync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            previous = activeReveal;
            activeReveal = next;
        }

        previous?.Cancel();
        return next;
    }

    private void EndReveal(CancellationTokenSource reveal)
    {
        lock (revealSync)
        {
            if (ReferenceEquals(activeReveal, reveal))
                activeReveal = null;
        }

        reveal.Dispose();
    }

    private async Task WriteDiagnosticsAsync(string category, long started)
    {
        try
        {
            await diagnostics.WriteAsync(category, time.GetElapsedTime(started), 1, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must never turn a recoverable activation warning into a failure.
        }
    }

    private static async Task AwaitOperationsAsync(IReadOnlyCollection<Task> operations)
    {
        try
        {
            await Task.WhenAll(operations).ConfigureAwait(false);
        }
        catch
        {
            // Shutdown waits for all operations but does not surface their work errors.
        }
    }
}
