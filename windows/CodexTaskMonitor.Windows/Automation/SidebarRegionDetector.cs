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

        var directContainers = items.Select(node => node.AncestorRuntimeIds.LastOrDefault()).Distinct(StringComparer.Ordinal).ToArray();
        if (directContainers.Length != 1 || string.IsNullOrEmpty(directContainers[0]))
            return null;
        var containerId = directContainers[0]!;
        if (snapshot.Nodes.Count(node => node.RuntimeId == containerId && node.ControlType == "ControlType.List") != 1)
            return null;

        var left = items.Min(node => node.Bounds.Left);
        var top = items.Min(node => node.Bounds.Top);
        var right = items.Max(node => node.Bounds.Right);
        var bottom = items.Max(node => node.Bounds.Bottom);
        var region = new Rect(left, top, right - left, bottom - top);
        if (region.Height < 120 || region.Width < 120)
            return null;

        var anchor = items.OrderBy(node => Math.Abs(node.Bounds.Top + node.Bounds.Height / 2 - (region.Top + region.Height / 2))).First();
        if (items.Any(node => node.NativeWindowHandle != anchor.NativeWindowHandle))
            return null;
        var inputPoint = new Point(
            Math.Min(anchor.Bounds.Right - 1, anchor.Bounds.Left + Math.Min(12, anchor.Bounds.Width / 2)),
            anchor.Bounds.Top + anchor.Bounds.Height / 2);
        return anchor.Bounds.Contains(inputPoint)
            ? new SidebarScrollRegion(region, inputPoint, containerId, anchor.RuntimeId, anchor.NativeWindowHandle)
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

}
