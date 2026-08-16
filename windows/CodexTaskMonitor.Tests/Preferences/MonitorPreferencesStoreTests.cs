using System.Text.Json;
using CodexTaskMonitor.Core.Preferences;

namespace CodexTaskMonitor.Tests.Preferences;

public sealed class MonitorPreferencesStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsExactSets()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
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
            Assert.Empty(Directory.EnumerateFiles(directory, "settings.json*.tmp"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task MissingFile_ReturnsEmptyPreferences()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new MonitorPreferencesStore(Path.Combine(directory, "settings.json"));

            Assert.Equal(MonitorPreferences.Empty, await store.LoadAsync(default));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task MalformedJson_IsReportedInsteadOfDiscarded()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, "{not-json");
            var store = new MonitorPreferencesStore(path);

            await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync(default));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
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

    [Theory]
    [InlineData("turn")]
    [InlineData(":turn")]
    [InlineData("thread:")]
    [InlineData("thread:turn:extra")]
    [InlineData("thread :turn")]
    [InlineData("thread: turn")]
    public void Constructor_RejectsAmbiguousPersistedHandledItemKeys(string itemId) =>
        Assert.Throws<ArgumentException>(() => new MonitorPreferences(null, [], [], [itemId], null, null, null));

    [Fact]
    public async Task Load_InvalidPersistedHandledItemKey_ReportsDataErrorWithoutLeakingTheKey()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            const string invalidKey = "missing-separator";
            await File.WriteAllTextAsync(path, $$"""{"dismissedItemIds":["{{invalidKey}}"]}""");
            var store = new MonitorPreferencesStore(path);

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(default));

            Assert.DoesNotContain(invalidKey, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task ConcurrentSaves_PersistOneCompleteSnapshotAndLeaveNoTemporaryFiles()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var firstStore = new MonitorPreferencesStore(path);
            var secondStore = new MonitorPreferencesStore(path);
            var preferences = Enumerable.Range(0, 8)
                .Select(index => new MonitorPreferences(
                    DateTimeOffset.FromUnixTimeSeconds(index),
                    Enumerable.Range(0, 8_192).Select(value => $"turn-{index}-{value}"),
                    [],
                    [$"thread-{index}:turn-{index}"],
                    null,
                    null,
                    null))
                .ToArray();
            using var ready = new CountdownEvent(preferences.Length);
            using var start = new ManualResetEventSlim();
            var saves = preferences.Select((value, index) => Task.Factory.StartNew(
                async () =>
                {
                    ready.Signal();
                    start.Wait();
                    await (index % 2 == 0 ? firstStore : secondStore).SaveAsync(value, default);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap()).ToArray();

            ready.Wait();
            start.Set();
            await Task.WhenAll(saves);

            var actual = await firstStore.LoadAsync(default);
            Assert.Contains(preferences, expected => expected.AdoptedTurnIds.SetEquals(actual.AdoptedTurnIds));
            Assert.Empty(Directory.EnumerateFiles(directory, "settings.json*.tmp"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task ConcurrentLoadsAndSaves_AlwaysObserveCompletePreferences()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new MonitorPreferencesStore(path);
            var first = CreateLargePreferences(1);
            var second = CreateLargePreferences(2);
            await store.SaveAsync(first, default);

            var saver = Task.Run(async () =>
            {
                for (var index = 0; index < 4; index++)
                    await store.SaveAsync(index % 2 == 0 ? second : first, default);
            });
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                for (var index = 0; index < 4; index++)
                {
                    var actual = await store.LoadAsync(default);
                    Assert.True(
                        first.AdoptedTurnIds.SetEquals(actual.AdoptedTurnIds) ||
                        second.AdoptedTurnIds.SetEquals(actual.AdoptedTurnIds));
                }
            }));

            await Task.WhenAll(readers.Append(saver));
            Assert.Empty(Directory.EnumerateFiles(directory, "settings.json*.tmp"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static MonitorPreferences CreateLargePreferences(int version) =>
        new(
            DateTimeOffset.FromUnixTimeSeconds(version),
            Enumerable.Range(0, 65_536).Select(index => $"turn-{version}-{index}"),
            ["legacy-turn"],
            [$"thread-{version}:turn-{version}"],
            null,
            null,
            null);

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"preferences-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
