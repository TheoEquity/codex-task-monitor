using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class SidebarScrollController
{
    private const int AbsoluteMaximumSteps = 80;
    private static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromSeconds(8);

    private readonly ISidebarScrollInput input;
    private readonly int maxSteps;
    private readonly IUiAutomationSnapshotProvider snapshots;
    private readonly TimeProvider time;
    private readonly TimeSpan settleDelay;
    private readonly TimeSpan timeout;

    internal SidebarScrollController(
        IUiAutomationSnapshotProvider snapshots,
        ISidebarScrollInput input,
        TimeProvider time,
        TimeSpan settleDelay,
        int maxSteps,
        TimeSpan timeout)
    {
        this.snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        if (settleDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(settleDelay));
        if (maxSteps < 1)
            throw new ArgumentOutOfRangeException(nameof(maxSteps));
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        this.settleDelay = settleDelay;
        this.maxSteps = Math.Min(maxSteps, AbsoluteMaximumSteps);
        this.timeout = timeout < AbsoluteTimeout ? timeout : AbsoluteTimeout;
    }

    public async Task<SidebarScrollResult> RevealAsync(nint handle, SidebarTarget target, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        token.ThrowIfCancellationRequested();

        var started = time.GetTimestamp();
        var effects = new ScrollEffectAuthorization(time, started, timeout, token);
        var steps = 0;
        try
        {
            var snapshot = await CaptureAsync(handle, started, token);
            var initial = MatchVisible(snapshot, target);
            if (initial is not null)
                return initial;

            var region = SidebarRegionDetector.Detect(snapshot);
            if (region is null)
                return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);

            var modes = new[]
            {
                SidebarInputMode.AutomationPattern,
                SidebarInputMode.PostedMessage,
                SidebarInputMode.PhysicalFallback
            };
            SidebarInputMode? selectedMode = null;
            var anyInputAccepted = false;

            foreach (var mode in modes)
            {
                var stableAtTop = 0;
                var previous = SidebarRegionDetector.Signature(snapshot, region);
                while (stableAtTop < 2)
                {
                    ThrowIfAtLimit(started, steps);
                    steps++;
                    if (!await ScrollAsync(handle, region, ScrollDirection.Up, mode, effects, started, token))
                        break;
                    anyInputAccepted = true;
                    await SettleAsync(started, token);
                    snapshot = await CaptureAsync(handle, started, token);
                    var observed = MatchVisible(snapshot, target);
                    if (observed is not null)
                        return observed;
                    region = SidebarRegionDetector.Detect(snapshot);
                    if (region is null)
                        return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
                    var signature = SidebarRegionDetector.Signature(snapshot, region);
                    stableAtTop = signature == previous ? stableAtTop + 1 : 0;
                    previous = signature;
                }

                if (stableAtTop < 2)
                    continue;

                var stableProbe = 0;
                var probeMoved = false;
                while (stableProbe < 2 && !probeMoved)
                {
                    ThrowIfAtLimit(started, steps);
                    steps++;
                    if (!await ScrollAsync(handle, region, ScrollDirection.Down, mode, effects, started, token))
                        break;
                    anyInputAccepted = true;
                    await SettleAsync(started, token);
                    snapshot = await CaptureAsync(handle, started, token);
                    var probeMatch = MatchVisible(snapshot, target);
                    if (probeMatch is not null)
                        return probeMatch;
                    region = SidebarRegionDetector.Detect(snapshot);
                    if (region is null)
                        return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
                    var signature = SidebarRegionDetector.Signature(snapshot, region);
                    stableProbe = signature == previous ? stableProbe + 1 : 0;
                    probeMoved = signature != previous;
                    previous = signature;
                }

                if (!probeMoved)
                    continue;

                var resetStable = 0;
                previous = SidebarRegionDetector.Signature(snapshot, region);
                while (resetStable < 2)
                {
                    ThrowIfAtLimit(started, steps);
                    steps++;
                    if (!await ScrollAsync(handle, region, ScrollDirection.Up, mode, effects, started, token))
                        return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
                    await SettleAsync(started, token);
                    snapshot = await CaptureAsync(handle, started, token);
                    var resetMatch = MatchVisible(snapshot, target);
                    if (resetMatch is not null)
                        return resetMatch;
                    region = SidebarRegionDetector.Detect(snapshot);
                    if (region is null)
                        return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
                    var signature = SidebarRegionDetector.Signature(snapshot, region);
                    resetStable = signature == previous ? resetStable + 1 : 0;
                    previous = signature;
                }

                selectedMode = mode;
                break;
            }

            if (selectedMode is null)
                return new SidebarScrollResult(anyInputAccepted ? SidebarScrollStatus.NotFound : SidebarScrollStatus.RegionUnavailable, null);

            var stableAtBottom = 0;
            var lastSignature = SidebarRegionDetector.Signature(snapshot, region);
            while (true)
            {
                var match = MatchVisible(snapshot, target);
                if (match is not null)
                    return match;

                ThrowIfAtLimit(started, steps);
                steps++;
                if (!await ScrollAsync(handle, region, ScrollDirection.Down, selectedMode.Value, effects, started, token))
                    return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
                await SettleAsync(started, token);
                snapshot = await CaptureAsync(handle, started, token);
                var observed = MatchVisible(snapshot, target);
                if (observed is not null)
                    return observed;
                region = SidebarRegionDetector.Detect(snapshot);
                if (region is null)
                    return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
                var signature = SidebarRegionDetector.Signature(snapshot, region);
                stableAtBottom = signature == lastSignature ? stableAtBottom + 1 : 0;
                if (stableAtBottom >= 2)
                    return new SidebarScrollResult(SidebarScrollStatus.NotFound, null);
                lastSignature = signature;
            }
        }
        catch (DeadlineExceededException)
        {
            return new SidebarScrollResult(SidebarScrollStatus.TimedOut, null);
        }
        catch (StepLimitReachedException)
        {
            return new SidebarScrollResult(SidebarScrollStatus.NotFound, null);
        }
        finally
        {
            effects.Expire();
        }
    }

    private async Task<AutomationSnapshot> CaptureAsync(nint handle, long started, CancellationToken token) =>
        await AwaitWithinDeadlineAsync(inner => snapshots.CaptureAsync(handle, inner), started, token);

    private async Task<bool> ScrollAsync(
        nint handle,
        SidebarScrollRegion region,
        ScrollDirection direction,
        SidebarInputMode mode,
        ScrollEffectAuthorization effects,
        long started,
        CancellationToken token) =>
        await AwaitWithinDeadlineAsync(inner => input.ScrollAsync(handle, region, direction, mode, effects.CreatePermit(), inner), started, token);

    private async Task SettleAsync(long started, CancellationToken token) =>
        _ = await AwaitWithinDeadlineAsync(async inner =>
        {
            await Task.Delay(settleDelay, time, inner);
            return true;
        }, started, token);

    private async Task<T> AwaitWithinDeadlineAsync<T>(Func<CancellationToken, Task<T>> operation, long started, CancellationToken token)
    {
        var remaining = timeout - time.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero)
            throw new DeadlineExceededException();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var pending = Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                return operation(linked.Token);
            }, CancellationToken.None);
            return await pending.WaitAsync(remaining, time, token);
        }
        catch (TimeoutException)
        {
            linked.Cancel();
            throw new DeadlineExceededException();
        }
    }

    private void ThrowIfAtLimit(long started, int steps)
    {
        if (time.GetElapsedTime(started) >= timeout)
            throw new DeadlineExceededException();
        if (steps >= maxSteps)
            throw new StepLimitReachedException();
    }

    private static SidebarScrollResult? MatchVisible(AutomationSnapshot snapshot, SidebarTarget target)
    {
        var match = SidebarMatcher.Match(snapshot, target);
        return match.Status switch
        {
            SidebarMatchStatus.Ambiguous => new SidebarScrollResult(SidebarScrollStatus.Ambiguous, null),
            SidebarMatchStatus.Found when match.Node is { IsOffscreen: false } visible => new SidebarScrollResult(SidebarScrollStatus.Found, visible),
            _ => null
        };
    }

    private sealed class DeadlineExceededException : Exception;
    private sealed class StepLimitReachedException : Exception;
}
