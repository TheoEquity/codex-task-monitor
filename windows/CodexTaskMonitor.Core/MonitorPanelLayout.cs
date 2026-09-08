namespace CodexTaskMonitor.Core;

public static class MonitorPanelLayout
{
    public static double Height(int itemCount, bool hasError, int groupCount = 0)
    {
        var items = Math.Max(itemCount, 0);
        var groups = Math.Min(Math.Max(groupCount, 0), items);
        var listHeight = Math.Min(items * 63 + groups * 24, 378);
        return 48 + listHeight + (hasError ? 32 : 0);
    }
}
