using System.Windows;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Windows.Interop;

public sealed class NativeSidebarWheelInput
{
    private readonly INativeSidebarWheelApi api;

    public NativeSidebarWheelInput() : this(new WindowsNativeSidebarWheelApi())
    {
    }

    internal NativeSidebarWheelInput(INativeSidebarWheelApi api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public Task<bool> ScrollAsync(nint handle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var delta = direction == ScrollDirection.Up ? 240 : -240;

        return mode switch
        {
            SidebarInputMode.PostedMessage => Task.FromResult(ScrollPosted(handle, region, delta)),
            SidebarInputMode.PhysicalFallback => Task.FromResult(ScrollPhysical(handle, region, delta, token)),
            _ => Task.FromResult(false)
        };
    }

    private bool ScrollPosted(nint handle, SidebarScrollRegion region, int delta) =>
        TryValidatePoint(handle, region, out var hitTestWindow) && api.PostWheel(hitTestWindow, region.InputPoint, delta);

    private bool ScrollPhysical(nint handle, SidebarScrollRegion region, int delta, CancellationToken token)
    {
        var originalForeground = api.GetForegroundWindow();
        if (!api.GetCursorPosition(out var originalCursor))
            return false;

        try
        {
            if (originalForeground != handle || !TryValidatePoint(handle, region, out _))
                return false;
            token.ThrowIfCancellationRequested();
            if (!api.SetCursorPosition(region.InputPoint))
                return false;
            if (api.GetForegroundWindow() != handle || !TryValidatePoint(handle, region, out _))
                return false;
            token.ThrowIfCancellationRequested();
            return api.SendWheel(delta);
        }
        finally
        {
            _ = api.SetCursorPosition(originalCursor);
            if (originalForeground != 0 && api.GetForegroundWindow() != originalForeground)
                _ = api.SetForegroundWindow(originalForeground);
        }
    }

    private bool TryValidatePoint(nint rootHandle, SidebarScrollRegion region, out nint hitTestWindow)
    {
        hitTestWindow = api.WindowFromPoint(region.InputPoint);
        return region.ExpectedHitTestWindow != 0 && hitTestWindow == region.ExpectedHitTestWindow &&
            api.IsWindowOwnedBy(rootHandle, hitTestWindow);
    }
}
