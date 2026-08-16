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
    bool TryExecute(Action effect);
}

internal sealed class ScrollEffectAuthorization(TimeProvider time, long started, TimeSpan timeout, CancellationToken token)
{
    private const int Pending = 0;
    private const int Executing = 1;
    private const int Expired = 2;
    private int state;

    public IScrollEffectPermit CreatePermit() => new Permit(this);

    public void Expire() => Interlocked.Exchange(ref state, Expired);

    private bool TryExecute(Action effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (token.IsCancellationRequested || time.GetElapsedTime(started) >= timeout)
        {
            Expire();
            return false;
        }

        if (Interlocked.CompareExchange(ref state, Executing, Pending) != Pending)
            return false;

        try
        {
            effect();
            return true;
        }
        finally
        {
            _ = Interlocked.CompareExchange(ref state, Pending, Executing);
        }
    }

    private sealed class Permit(ScrollEffectAuthorization owner) : IScrollEffectPermit
    {
        private int used;

        public bool TryExecute(Action effect) =>
            Interlocked.CompareExchange(ref used, 1, 0) == 0 && owner.TryExecute(effect);
    }
}
