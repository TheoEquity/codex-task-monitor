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

    public SidebarScrollController(
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
        var steps = 0;
        var snapshot = await CaptureUntilDeadlineAsync(handle, started, token);
        if (snapshot is null)
            return new SidebarScrollResult(SidebarScrollStatus.TimedOut, null);
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
            var previous = SidebarRegionDetector.Signature(snapshot, region.Value);
            while (stableAtTop < 2)
            {
                var limit = CheckLimits(started, steps);
                if (limit is not null)
                    return limit;

                steps++;
                if (!await input.ScrollAsync(handle, region.Value, ScrollDirection.Up, mode, token))
                    break;
                anyInputAccepted = true;
                await Task.Delay(settleDelay, time, token);
                snapshot = await CaptureUntilDeadlineAsync(handle, started, token);
                if (snapshot is null)
                    return new SidebarScrollResult(SidebarScrollStatus.TimedOut, null);
                var observed = MatchVisible(snapshot, target);
                if (observed is not null)
                    return observed;
                region = SidebarRegionDetector.Detect(snapshot);
                if (region is null)
                    return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
                var signature = SidebarRegionDetector.Signature(snapshot, region.Value);
                stableAtTop = signature == previous ? stableAtTop + 1 : 0;
                previous = signature;
            }

            if (stableAtTop < 2)
                continue;

            var limitAtTop = CheckLimits(started, steps);
            if (limitAtTop is not null)
                return limitAtTop;
            steps++;
            if (!await input.ScrollAsync(handle, region.Value, ScrollDirection.Down, mode, token))
                continue;
            anyInputAccepted = true;
            await Task.Delay(settleDelay, time, token);
            var probe = await CaptureUntilDeadlineAsync(handle, started, token);
            if (probe is null)
                return new SidebarScrollResult(SidebarScrollStatus.TimedOut, null);
            var probeMatch = MatchVisible(probe, target);
            if (probeMatch is not null)
                return probeMatch;
            region = SidebarRegionDetector.Detect(probe);
            if (region is null)
                return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
            if (SidebarRegionDetector.Signature(probe, region.Value) == previous)
                continue;

            snapshot = probe;
            var resetStable = 0;
            previous = SidebarRegionDetector.Signature(snapshot, region.Value);
            while (resetStable < 2)
            {
                var resetLimit = CheckLimits(started, steps);
                if (resetLimit is not null)
                    return resetLimit;

                steps++;
                if (!await input.ScrollAsync(handle, region.Value, ScrollDirection.Up, mode, token))
                    return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
                await Task.Delay(settleDelay, time, token);
                snapshot = await CaptureUntilDeadlineAsync(handle, started, token);
                if (snapshot is null)
                    return new SidebarScrollResult(SidebarScrollStatus.TimedOut, null);
                var resetMatch = MatchVisible(snapshot, target);
                if (resetMatch is not null)
                    return resetMatch;
                region = SidebarRegionDetector.Detect(snapshot);
                if (region is null)
                    return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
                var signature = SidebarRegionDetector.Signature(snapshot, region.Value);
                resetStable = signature == previous ? resetStable + 1 : 0;
                previous = signature;
            }

            selectedMode = mode;
            break;
        }

        if (selectedMode is null)
            return new SidebarScrollResult(anyInputAccepted ? SidebarScrollStatus.NotFound : SidebarScrollStatus.RegionUnavailable, null);

        var stableAtBottom = 0;
        var lastSignature = SidebarRegionDetector.Signature(snapshot, region.Value);
        while (true)
        {
            var match = MatchVisible(snapshot, target);
            if (match is not null)
                return match;

            var limit = CheckLimits(started, steps);
            if (limit is not null)
                return limit;
            steps++;
            if (!await input.ScrollAsync(handle, region.Value, ScrollDirection.Down, selectedMode.Value, token))
                return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
            await Task.Delay(settleDelay, time, token);
            snapshot = await CaptureUntilDeadlineAsync(handle, started, token);
            if (snapshot is null)
                return new SidebarScrollResult(SidebarScrollStatus.TimedOut, null);
            var observed = MatchVisible(snapshot, target);
            if (observed is not null)
                return observed;
            region = SidebarRegionDetector.Detect(snapshot);
            if (region is null)
                return new SidebarScrollResult(SidebarScrollStatus.RegionUnavailable, null);
            var signature = SidebarRegionDetector.Signature(snapshot, region.Value);
            stableAtBottom = signature == lastSignature ? stableAtBottom + 1 : 0;
            if (stableAtBottom >= 2)
                return new SidebarScrollResult(SidebarScrollStatus.NotFound, null);
            lastSignature = signature;
        }
    }

    private SidebarScrollResult? CheckLimits(long started, int steps)
    {
        if (time.GetElapsedTime(started) >= timeout)
            return new SidebarScrollResult(SidebarScrollStatus.TimedOut, null);
        return steps >= maxSteps
            ? new SidebarScrollResult(SidebarScrollStatus.NotFound, null)
            : null;
    }

    private async Task<AutomationSnapshot?> CaptureUntilDeadlineAsync(nint handle, long started, CancellationToken token)
    {
        var remaining = timeout - time.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero)
            return null;

        try
        {
            return await snapshots.CaptureAsync(handle, token).WaitAsync(remaining, time, token);
        }
        catch (TimeoutException)
        {
            return null;
        }
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
}
