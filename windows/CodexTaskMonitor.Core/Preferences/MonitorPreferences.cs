using System.Collections.Frozen;

namespace CodexTaskMonitor.Core.Preferences;

public sealed record MonitorPreferences
{
    public DateTimeOffset? Baseline { get; private init; }
    public IReadOnlySet<string> AdoptedTurnIds { get; private init; }
    public IReadOnlySet<string> DismissedTurnIds { get; private init; }
    public IReadOnlySet<string> DismissedItemIds { get; private init; }
    public double? WindowLeft { get; private init; }
    public double? WindowTop { get; private init; }
    public bool? LaunchAtLoginEnabled { get; private init; }

    public MonitorPreferences(
        DateTimeOffset? baseline,
        IEnumerable<string> adoptedTurnIds,
        IEnumerable<string> dismissedTurnIds,
        IEnumerable<string> dismissedItemIds,
        double? windowLeft,
        double? windowTop,
        bool? launchAtLoginEnabled)
    {
        Baseline = baseline;
        AdoptedTurnIds = Freeze(adoptedTurnIds);
        DismissedTurnIds = Freeze(dismissedTurnIds);
        DismissedItemIds = Freeze(dismissedItemIds);
        WindowLeft = windowLeft;
        WindowTop = windowTop;
        LaunchAtLoginEnabled = launchAtLoginEnabled;
    }

    public static MonitorPreferences Empty { get; } = new(null, [], [], [], null, null, null);

    public MonitorPreferences Initialize(DateTimeOffset baseline, IEnumerable<string> adopted) =>
        Baseline is null
            ? this with { Baseline = baseline, AdoptedTurnIds = Freeze(adopted) }
            : this;

    public MonitorPreferences Dismiss(string itemId)
    {
        if (!IsExactHandledItemKey(itemId))
            throw new ArgumentException("A handled item key must be an exact threadId:turnId pair.", nameof(itemId));

        var updated = DismissedItemIds.ToHashSet(StringComparer.Ordinal);
        updated.Add(itemId);
        return this with { DismissedItemIds = Freeze(updated) };
    }

    public MonitorPreferences WithWindowPosition(double left, double top) =>
        this with { WindowLeft = left, WindowTop = top };

    public MonitorPreferences WithLaunchAtLogin(bool enabled) =>
        this with { LaunchAtLoginEnabled = enabled };

    private static bool IsExactHandledItemKey(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) || itemId.Any(char.IsWhiteSpace))
            return false;

        var separator = itemId.IndexOf(':');
        return separator > 0 && separator == itemId.LastIndexOf(':') && separator < itemId.Length - 1;
    }

    private static IReadOnlySet<string> Freeze(IEnumerable<string> values) =>
        values.ToFrozenSet(StringComparer.Ordinal);
}
