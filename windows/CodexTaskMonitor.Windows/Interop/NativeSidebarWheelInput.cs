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

    internal Task<bool> ScrollAsync(nint handle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, IScrollEffectPermit permit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var delta = direction == ScrollDirection.Up ? 240 : -240;

        return mode switch
        {
            SidebarInputMode.PostedMessage => Task.FromResult(ScrollPosted(handle, region, delta, permit)),
            SidebarInputMode.PhysicalFallback => Task.FromResult(ScrollPhysical(handle, region, delta, permit, token)),
            _ => Task.FromResult(false)
        };
    }

    private bool ScrollPosted(nint handle, SidebarScrollRegion region, int delta, IScrollEffectPermit permit)
    {
        if (!TryValidatePoint(handle, region, out var hitTestWindow))
            return false;
        var posted = false;
        return permit.TryExecute(() => posted = api.PostWheel(hitTestWindow, region.InputPoint, delta)) && posted;
    }

    private bool ScrollPhysical(nint handle, SidebarScrollRegion region, int delta, IScrollEffectPermit permit, CancellationToken token)
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
            var sent = false;
            return permit.TryExecute(() => sent = api.SendWheel(delta)) && sent;
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
