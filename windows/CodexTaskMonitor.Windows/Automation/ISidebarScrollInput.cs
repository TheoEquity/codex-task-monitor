using System.Windows;

namespace CodexTaskMonitor.Windows.Automation;

public enum ScrollDirection
{
    Up,
    Down
}

public enum SidebarInputMode
{
    AutomationPattern,
    PostedMessage,
    PhysicalFallback
}

public enum SidebarScrollStatus
{
    Found,
    NotFound,
    Ambiguous,
    RegionUnavailable,
    TimedOut
}

public sealed record SidebarScrollResult(SidebarScrollStatus Status, AutomationNode? Node);

public sealed record SidebarScrollRegion(
    Rect Bounds,
    Point InputPoint,
    string ContainerRuntimeId,
    string InputNodeRuntimeId,
    nint ExpectedHitTestWindow);

internal interface ISidebarScrollInput
{
    Task<bool> ScrollAsync(nint windowHandle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, IScrollEffectPermit permit, CancellationToken token);
}

internal interface IScrollEffectPermit
{
    bool TryAuthorize();
}

internal sealed class ScrollEffectAuthorization(TimeProvider time, long started, TimeSpan timeout, CancellationToken token)
{
    private int expired;

    public IScrollEffectPermit CreatePermit() => new Permit(this);

    public void Expire() => Interlocked.Exchange(ref expired, 1);

    private bool TryAuthorize()
    {
        if (token.IsCancellationRequested || time.GetElapsedTime(started) >= timeout)
        {
            Expire();
            return false;
        }

        return Volatile.Read(ref expired) == 0;
    }

    private sealed class Permit(ScrollEffectAuthorization owner) : IScrollEffectPermit
    {
        private int used;

        public bool TryAuthorize() =>
            Interlocked.CompareExchange(ref used, 1, 0) == 0 && owner.TryAuthorize();
    }
}
