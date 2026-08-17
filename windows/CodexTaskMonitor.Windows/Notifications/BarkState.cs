using System.Collections.Frozen;

namespace CodexTaskMonitor.Windows.Notifications;

public sealed record BarkState
{
    public bool Enabled { get; private init; }
    public Guid ConfigurationId { get; private init; }
    public DateTimeOffset? EnabledAt { get; private init; }
    public IReadOnlySet<string> NotifiedItemIds { get; private init; }

    public BarkState(
        bool enabled,
        Guid configurationId,
        DateTimeOffset? enabledAt,
        IEnumerable<string> notifiedItemIds)
    {
        Enabled = enabled;
        ConfigurationId = configurationId;
        EnabledAt = enabledAt;
        NotifiedItemIds = notifiedItemIds.ToFrozenSet(StringComparer.Ordinal);
    }

    public static BarkState Disabled { get; } = new(false, Guid.Empty, null, []);

    public BarkState MarkNotified(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        var updated = NotifiedItemIds.ToHashSet(StringComparer.Ordinal);
        updated.Add(itemId);
        return this with { NotifiedItemIds = updated.ToFrozenSet(StringComparer.Ordinal) };
    }
}

public sealed record BarkSecret(Guid ConfigurationId, Uri Endpoint);

public sealed record BarkNotification(string Title, string Body, string Group);
