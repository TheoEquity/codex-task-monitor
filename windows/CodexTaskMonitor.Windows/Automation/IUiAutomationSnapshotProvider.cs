namespace CodexTaskMonitor.Windows.Automation;

public interface IUiAutomationSnapshotProvider
{
    Task<AutomationSnapshot> CaptureAsync(nint windowHandle, CancellationToken token);
}

public interface IChatGptWindowLocator
{
    nint FindMainWindow();
}
