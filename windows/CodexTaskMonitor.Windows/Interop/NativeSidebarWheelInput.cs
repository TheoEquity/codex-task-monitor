using System.Windows;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Windows.Interop;

public sealed class NativeSidebarWheelInput
{
    public Task<bool> ScrollAsync(nint handle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var point = new NativeMethods.Point((int)region.Left + 12, (int)region.Top + (int)region.Height / 2);
        var delta = direction == ScrollDirection.Up ? 240 : -240;

        return mode switch
        {
            SidebarInputMode.PostedMessage => Task.FromResult(ScrollPosted(handle, point, delta)),
            SidebarInputMode.PhysicalFallback => Task.FromResult(ScrollPhysical(handle, point, delta, token)),
            _ => Task.FromResult(false)
        };
    }

    private static bool ScrollPosted(nint handle, NativeMethods.Point point, int delta) =>
        NativeMethods.IsPointInWindow(handle, point) && NativeMethods.PostWheel(handle, point, delta);

    private static bool ScrollPhysical(nint handle, NativeMethods.Point point, int delta, CancellationToken token)
    {
        if (!NativeMethods.GetCursorPos(out var originalCursor))
            return false;

        var originalForeground = NativeMethods.GetForegroundWindow();
        try
        {
            if (originalForeground != handle || !NativeMethods.IsPointInWindow(handle, point))
                return false;
            token.ThrowIfCancellationRequested();
            if (!NativeMethods.SetCursorPos(point.X, point.Y))
                return false;
            if (NativeMethods.GetForegroundWindow() != handle || !NativeMethods.IsPointInWindow(handle, point))
                return false;
            token.ThrowIfCancellationRequested();
            return NativeMethods.SendWheel(delta);
        }
        finally
        {
            _ = NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
            if (originalForeground != 0 && NativeMethods.GetForegroundWindow() != originalForeground)
                _ = NativeMethods.SetForegroundWindow(originalForeground);
        }
    }
}
