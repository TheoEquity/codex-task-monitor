namespace CodexTaskMonitor.Core;

public static class MonitorPanelLayout
{
    public static double Height(int itemCount, bool hasError)
    {
        var rows = Math.Min(Math.Max(itemCount, 0), 6);
        var dividers = rows > 0 ? rows : 0;
        return 48 + rows * 62 + dividers + (hasError ? 32 : 0);
    }
}
