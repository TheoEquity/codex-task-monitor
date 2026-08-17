namespace CodexTaskMonitor.Core.Monitoring;

public sealed record MonitorItem(
    string ThreadId,
    string TurnId,
    string Title,
    string Cwd,
    string ProjectName,
    DateTimeOffset EventDate,
    TaskState State,
    TaskTerminalKind? TerminalKind = null)
{
    public string Id => $"{ThreadId}:{TurnId}";
}
