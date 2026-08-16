namespace CodexTaskMonitor.Core.Monitoring;

public static class TaskStateResolver
{
    public static TaskState? Resolve(
        LifecycleEvent item,
        DateTimeOffset baseline,
        IReadOnlySet<string> adoptedTurnIds,
        IReadOnlySet<string> dismissedTurnIds)
    {
        var roundedBaseline = DateTimeOffset.FromUnixTimeSeconds(baseline.ToUnixTimeSeconds());
        var crossesBaseline = item.CompletedAt is { } completed && completed >= roundedBaseline;
        if (item.StartedAt < roundedBaseline && !crossesBaseline && !adoptedTurnIds.Contains(item.TurnId))
            return null;

        return item.Kind switch
        {
            LifecycleKind.Started => TaskState.Running,
            LifecycleKind.Completed or LifecycleKind.Aborted when dismissedTurnIds.Contains(item.TurnId) => null,
            LifecycleKind.Completed or LifecycleKind.Aborted => TaskState.Waiting,
            _ => null
        };
    }
}
