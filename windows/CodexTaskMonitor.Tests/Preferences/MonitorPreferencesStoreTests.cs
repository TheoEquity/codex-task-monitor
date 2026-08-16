using System.Text.Json;
using CodexTaskMonitor.Core.Preferences;

namespace CodexTaskMonitor.Tests.Preferences;

public sealed class MonitorPreferencesStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsExactSets()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var store = new MonitorPreferencesStore(path);
        var expected = new MonitorPreferences(
            DateTimeOffset.FromUnixTimeSeconds(100), ["adopted"], ["legacy"], ["thread:turn"], 120, 240, false);

        await store.SaveAsync(expected, default);
        var actual = await store.LoadAsync(default);

        Assert.Equal(expected.Baseline, actual.Baseline);
        Assert.True(expected.AdoptedTurnIds.SetEquals(actual.AdoptedTurnIds));
        Assert.True(expected.DismissedTurnIds.SetEquals(actual.DismissedTurnIds));
        Assert.True(expected.DismissedItemIds.SetEquals(actual.DismissedItemIds));
        Assert.Equal((120d, 240d), (actual.WindowLeft, actual.WindowTop));
        Assert.Equal(false, actual.LaunchAtLoginEnabled);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task MissingFile_ReturnsEmptyPreferences()
    {
        var store = new MonitorPreferencesStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));

        Assert.Equal(MonitorPreferences.Empty, await store.LoadAsync(default));
    }

    [Fact]
    public async Task MalformedJson_IsReportedInsteadOfDiscarded()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "{not-json");
        var store = new MonitorPreferencesStore(path);

        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync(default));
    }

    [Fact]
    public void Dismiss_UsesTheExactThreadAndTurnKey()
    {
        var updated = MonitorPreferences.Empty.Dismiss("thread-a:turn");

        Assert.Contains("thread-a:turn", updated.DismissedItemIds);
        Assert.DoesNotContain("thread-b:turn", updated.DismissedItemIds);
    }

    [Fact]
    public void Constructor_CopiesTheSuppliedSets()
    {
        var adopted = new HashSet<string>(StringComparer.Ordinal) { "turn-a" };
        var preferences = new MonitorPreferences(null, adopted, [], [], null, null, null);

        adopted.Add("turn-b");

        Assert.DoesNotContain("turn-b", preferences.AdoptedTurnIds);
    }

    [Fact]
    public void CollectionProperties_CannotBeReplacedByCallers()
    {
        var setter = typeof(MonitorPreferences)
            .GetProperty(nameof(MonitorPreferences.AdoptedTurnIds))!
            .GetSetMethod(nonPublic: true)!;

        Assert.False(setter.IsPublic);
    }

    [Fact]
    public void Initialize_SetsBaselineAndAdoptsTurnsOnlyOnce()
    {
        var baseline = DateTimeOffset.FromUnixTimeSeconds(100);
        var initialized = MonitorPreferences.Empty.Initialize(baseline, ["turn-a"]);
        var unchanged = initialized.Initialize(baseline.AddSeconds(1), ["turn-b"]);

        Assert.Equal(baseline, initialized.Baseline);
        Assert.Contains("turn-a", initialized.AdoptedTurnIds);
        Assert.Equal(initialized, unchanged);
    }

    [Theory]
    [InlineData("turn")]
    [InlineData(":turn")]
    [InlineData("thread:")]
    [InlineData("thread:turn:extra")]
    [InlineData("thread :turn")]
    [InlineData("thread: turn")]
    public void Dismiss_RejectsAmbiguousHandledItemKeys(string itemId) =>
        Assert.Throws<ArgumentException>(() => MonitorPreferences.Empty.Dismiss(itemId));
}
