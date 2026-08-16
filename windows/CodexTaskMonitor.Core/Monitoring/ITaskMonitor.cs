namespace CodexTaskMonitor.Core.Monitoring;

public sealed record MonitorScanOptions(
    DateTimeOffset Baseline,
    IReadOnlySet<string> AdoptedTurnIds,
    IReadOnlySet<string> DismissedTurnIds,
    IReadOnlySet<string> DismissedItemIds);

public sealed record MonitorScanResult(IReadOnlyList<MonitorItem> Items, int UnreadableRolloutCount);

public interface ITaskMonitor
{
    Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken cancellationToken);

    Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken cancellationToken);
}
