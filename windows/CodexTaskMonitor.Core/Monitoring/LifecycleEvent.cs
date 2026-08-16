namespace CodexTaskMonitor.Core.Monitoring;

public enum LifecycleKind { Started, Completed, Aborted }
public enum TaskState { Running, Waiting }

public sealed record LifecycleEvent(
    LifecycleKind Kind,
    string TurnId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt)
{
    public DateTimeOffset ActivityDate => CompletedAt ?? StartedAt;
}
