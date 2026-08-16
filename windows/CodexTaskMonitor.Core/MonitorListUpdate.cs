namespace CodexTaskMonitor.Core;

public static class MonitorListUpdate
{
    public static string? InsertedId(IReadOnlyList<string> oldIds, IReadOnlyList<string> newIds)
    {
        var removed = oldIds.Except(newIds, StringComparer.Ordinal).Any();
        var inserted = newIds.Except(oldIds, StringComparer.Ordinal).ToArray();
        return !removed && inserted.Length == 1 ? inserted[0] : null;
    }
}
