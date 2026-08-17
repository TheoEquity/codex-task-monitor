using CodexTaskMonitor.Windows.Notifications;

namespace CodexTaskMonitor.Tests.Notifications;

public sealed class BarkStateStoreTests
{
    [Fact]
    public async Task MissingFile_LoadsDisabledState()
    {
        var path = TemporaryPath();
        var store = new BarkStateStore(path);

        var state = await store.LoadAsync(default);

        Assert.False(state.Enabled);
        Assert.Equal(Guid.Empty, state.ConfigurationId);
        Assert.Null(state.EnabledAt);
        Assert.Empty(state.NotifiedItemIds);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsEnabledState()
    {
        var path = TemporaryPath();
        try
        {
            var store = new BarkStateStore(path);
            var expected = new BarkState(
                true,
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                DateTimeOffset.Parse("2026-08-17T20:00:00Z"),
                ["thread-1:turn-1"]);

            await store.SaveAsync(expected, default);

            var actual = await store.LoadAsync(default);
            Assert.Equal(expected.Enabled, actual.Enabled);
            Assert.Equal(expected.ConfigurationId, actual.ConfigurationId);
            Assert.Equal(expected.EnabledAt, actual.EnabledAt);
            Assert.Equal(expected.NotifiedItemIds.Order(), actual.NotifiedItemIds.Order());
        }
        finally
        {
            DeleteParent(path);
        }
    }

    [Fact]
    public async Task MarkNotifiedAsync_OnlyUpdatesMatchingConfiguration()
    {
        var path = TemporaryPath();
        try
        {
            var store = new BarkStateStore(path);
            var currentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            await store.SaveAsync(
                new BarkState(true, currentId, DateTimeOffset.Parse("2026-08-17T20:00:00Z"), []),
                default);

            var stale = await store.MarkNotifiedAsync(Guid.NewGuid(), "thread-1:turn-1", default);
            var committed = await store.MarkNotifiedAsync(currentId, "thread-1:turn-1", default);
            var duplicate = await store.MarkNotifiedAsync(currentId, "thread-1:turn-1", default);

            Assert.False(stale);
            Assert.True(committed);
            Assert.True(duplicate);
            Assert.Equal(["thread-1:turn-1"], (await store.LoadAsync(default)).NotifiedItemIds);
        }
        finally
        {
            DeleteParent(path);
        }
    }

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"bark-state-{Guid.NewGuid():N}", "bark-state.json");

    private static void DeleteParent(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
