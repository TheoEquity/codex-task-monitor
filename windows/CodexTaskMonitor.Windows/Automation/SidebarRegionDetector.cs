using System.Windows;

namespace CodexTaskMonitor.Windows.Automation;

public static class SidebarRegionDetector
{
    public static SidebarScrollRegion? Detect(AutomationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var leftLimit = snapshot.WindowBounds.Left + snapshot.WindowBounds.Width * 0.45;
        var items = snapshot.Nodes.Where(node =>
            node.ControlType == "ControlType.ListItem" && !node.IsOffscreen &&
            !node.Bounds.IsEmpty && snapshot.WindowBounds.Contains(node.Bounds) && node.Bounds.Left < leftLimit).ToArray();
        if (items.Length < 3)
            return null;

        var containerPath = CommonContainerPath(items);
        if (containerPath is null)
            return null;

        var expectedHitTestWindows = items.Select(node => node.NativeWindowHandle).Where(handle => handle != 0).Distinct().ToArray();
        if (expectedHitTestWindows.Length > 1)
            return null;

        var left = items.Min(node => node.Bounds.Left);
        var top = items.Min(node => node.Bounds.Top);
        var right = items.Max(node => node.Bounds.Right);
        var bottom = items.Max(node => node.Bounds.Bottom);
        var region = new Rect(left, top, right - left, bottom - top);
        if (region.Height < 120 || region.Width < 120)
            return null;

        var anchor = items.OrderBy(node => Math.Abs(node.Bounds.Top + node.Bounds.Height / 2 - (region.Top + region.Height / 2))).First();
        var inputPoint = new Point(
            Math.Min(anchor.Bounds.Right - 1, anchor.Bounds.Left + Math.Min(12, anchor.Bounds.Width / 2)),
            anchor.Bounds.Top + anchor.Bounds.Height / 2);
        return anchor.Bounds.Contains(inputPoint)
            ? new SidebarScrollRegion(region, inputPoint, containerPath[^1], anchor.RuntimeId, expectedHitTestWindows.SingleOrDefault())
            : null;
    }

    public static string Signature(AutomationSnapshot snapshot, SidebarScrollRegion region)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return string.Join('|', snapshot.Nodes
            .Where(node => node.ControlType == "ControlType.ListItem" && !node.IsOffscreen && region.Bounds.IntersectsWith(node.Bounds) &&
                node.AncestorRuntimeIds.Contains(region.ContainerRuntimeId, StringComparer.Ordinal))
            .OrderBy(node => node.Bounds.Top)
            .Select(node => $"{node.RuntimeId}:{node.Bounds.Top:0}"));
    }

    private static string[]? CommonContainerPath(IReadOnlyList<AutomationNode> items)
    {
        var common = items[0].AncestorRuntimeIds.ToArray();
        foreach (var item in items.Skip(1))
        {
            var length = 0;
            while (length < common.Length && length < item.AncestorRuntimeIds.Count && common[length] == item.AncestorRuntimeIds[length])
                length++;
            common = common[..length];
        }

        return common.Length >= 2 ? common : null;
    }
}
