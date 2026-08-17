using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Tests.Fixtures;

namespace CodexTaskMonitor.Tests.Data;

public sealed class SqliteThreadStoreTests
{
    [Fact]
    public async Task ReadThreads_ReturnsUserAndVisibleForkThreadsOnly()
    {
        await using var fixture = await CodexFixture.CreateAsync();
        await fixture.InsertThreadAsync("user", "User", "user", "vscode", archived: false, preview: "hello");
        await fixture.InsertThreadAsync("visible-fork", "Fork", "subagent", "vscode", archived: false, preview: "hello");
        await fixture.InsertThreadAsync("internal", "Internal", "subagent", "{\"subagent\":{}}", archived: false, preview: "hello");
        await fixture.InsertThreadAsync("unknown", "Unknown", "automation", "vscode", archived: false, preview: "hello");
        await fixture.InsertThreadAsync("archived", "Archived", "user", "vscode", archived: true, preview: "hello");
        await fixture.InsertThreadAsync("empty", "Empty", "user", "vscode", archived: false, preview: "");

        var records = await new SqliteThreadStore(fixture.DatabasePath).ReadThreadsAsync(DateTimeOffset.UnixEpoch, default);

        Assert.Equal(["user", "visible-fork"], records.Select(record => record.Id).Order().ToArray());
    }

    [Fact]
    public async Task MissingRequiredColumn_ThrowsFormatChanged()
    {
        await using var fixture = await CodexFixture.CreateAsync(includePreviewColumn: false);

        var error = await Assert.ThrowsAsync<CodexDataException>(() =>
            new SqliteThreadStore(fixture.DatabasePath).ReadThreadsAsync(DateTimeOffset.UnixEpoch, default));

        Assert.Equal(CodexDataError.FormatChanged, error.Error);
    }

    [Fact]
    public async Task UpdatedAfter_ExcludesOlderRows()
    {
        await using var fixture = await CodexFixture.CreateAsync();
        await fixture.InsertThreadAsync("old", "Old", "user", "vscode", false, "visible", updatedAtMs: 123_000);
        var store = new SqliteThreadStore(fixture.DatabasePath);

        Assert.Empty(await store.ReadThreadsAsync(DateTimeOffset.FromUnixTimeMilliseconds(124_000), default));
    }

    [Fact]
    public async Task PinnedAndSectionGrouping_AreReturnedAndCanBeLookedUp()
    {
        await using var fixture = await CodexFixture.CreateAsync();
        await fixture.InsertSectionAsync("research", "Research");
        await fixture.InsertThreadAsync("pinned", "Pinned", "user", "vscode", false, "visible", isPinned: true);
        await fixture.InsertThreadAsync("sectioned", "Sectioned", "user", "vscode", false, "visible", sectionId: "research");
        var store = new SqliteThreadStore(fixture.DatabasePath);
        var records = await store.ReadThreadsAsync(DateTimeOffset.UnixEpoch, default);
        var pinned = records.Single(record => record.Id == "pinned");
        var sectioned = records.Single(record => record.Id == "sectioned");

        Assert.True(pinned.Grouping.IsPinned);
        Assert.Equal("Research", sectioned.Grouping.SectionName);
        Assert.Equal(sectioned.Grouping, await store.FindGroupingAsync(sectioned.Id, default));
    }

    [Fact]
    public async Task MissingDatabase_ThrowsDatabaseMissingWithoutCreatingAFile()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"CodexTaskMonitor-{Guid.NewGuid():N}.sqlite");
        var store = new SqliteThreadStore(databasePath);

        var error = await Assert.ThrowsAsync<CodexDataException>(() =>
            store.ReadThreadsAsync(DateTimeOffset.UnixEpoch, default));

        Assert.Equal(CodexDataError.DatabaseMissing, error.Error);
        Assert.False(File.Exists(databasePath));
    }
}
