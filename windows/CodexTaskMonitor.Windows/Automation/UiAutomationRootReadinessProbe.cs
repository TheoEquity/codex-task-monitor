using System.Windows.Automation;

namespace CodexTaskMonitor.Windows.Automation;

public interface IUiAutomationRootReadinessProbe
{
    Task ProbeAsync(nint windowHandle, CancellationToken token);
}

public sealed class UiAutomationRootUnavailableException : Exception
{
    public UiAutomationRootUnavailableException()
        : base("ChatGPT UIA root is unavailable.")
    {
    }
}

public sealed class UiAutomationRootReadinessProbe : IUiAutomationRootReadinessProbe
{
    public Task ProbeAsync(nint windowHandle, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var root = AutomationElement.FromHandle(windowHandle)
                    ?? throw new UiAutomationRootUnavailableException();
                _ = root.Current.ProcessId;
                completion.TrySetResult();
            }
            catch (ElementNotAvailableException)
            {
                completion.TrySetException(new UiAutomationRootUnavailableException());
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(token);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "CodexTaskMonitor.UIA.RootReadiness"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
