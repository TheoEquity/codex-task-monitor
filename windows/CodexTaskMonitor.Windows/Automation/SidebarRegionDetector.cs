using System.Windows;

namespace CodexTaskMonitor.Windows.Automation;

public static class SidebarRegionDetector
{
    public static Rect? Detect(AutomationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var leftLimit = snapshot.WindowBounds.Left + snapshot.WindowBounds.Width * 0.45;
        var items = snapshot.Nodes.Where(node =>
            node.ControlType == "ControlType.ListItem" && !node.IsOffscreen &&
            !node.Bounds.IsEmpty && node.Bounds.Left < leftLimit).ToArray();
        if (items.Length < 3)
            return null;

        var left = items.Min(node => node.Bounds.Left);
        var top = items.Min(node => node.Bounds.Top);
        var right = items.Max(node => node.Bounds.Right);
        var bottom = items.Max(node => node.Bounds.Bottom);
        var region = new Rect(left, top, right - left, bottom - top);
        return region.Height >= 120 && region.Width >= 120 ? region : null;
    }

    public static string Signature(AutomationSnapshot snapshot, Rect region)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return string.Join('|', snapshot.Nodes
            .Where(node => node.ControlType == "ControlType.ListItem" && !node.IsOffscreen && region.IntersectsWith(node.Bounds))
            .OrderBy(node => node.Bounds.Top)
            .Select(node => $"{node.RuntimeId}:{node.Bounds.Top:0}"));
    }
}
