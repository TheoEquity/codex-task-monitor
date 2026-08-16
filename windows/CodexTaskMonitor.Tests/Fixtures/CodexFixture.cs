using Microsoft.Data.Sqlite;

namespace CodexTaskMonitor.Tests.Fixtures;

public sealed class CodexFixture : IAsyncDisposable
{
    private readonly string root;

    public string DatabasePath { get; }

    public string RolloutPath { get; }

    private CodexFixture(string root)
    {
        this.root = root;
        DatabasePath = Path.Combine(root, "state_5.sqlite");
        RolloutPath = Path.Combine(root, "rollout.jsonl");
    }

    public static async Task<CodexFixture> CreateAsync(bool includePreviewColumn = true)
    {
        var fixture = new CodexFixture(Path.Combine(Path.GetTempPath(), $"CodexTaskMonitor-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(fixture.root);
        await using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        var preview = includePreviewColumn ? ", preview TEXT NOT NULL DEFAULT ''" : "";
        var command = connection.CreateCommand();
        command.CommandText = $$"""
            CREATE TABLE thread_sections (id TEXT PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE threads (
              id TEXT PRIMARY KEY, rollout_path TEXT NOT NULL, cwd TEXT NOT NULL,
              title TEXT NOT NULL, archived INTEGER NOT NULL, updated_at_ms INTEGER,
              thread_source TEXT, source TEXT NOT NULL, is_pinned INTEGER NOT NULL DEFAULT 0,
              thread_section_id TEXT {{preview}}
            );
            """;
        await command.ExecuteNonQueryAsync();
        return fixture;
    }

    public async Task InsertThreadAsync(
        string id,
        string title,
        string threadSource,
        string source,
        bool archived,
        string preview,
        long updatedAtMs = 123_000,
        bool isPinned = false,
        string? sectionId = null)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO threads
              (id, rollout_path, cwd, title, archived, updated_at_ms, thread_source, source, is_pinned, thread_section_id, preview)
            VALUES
              ($id, $rollout, $cwd, $title, $archived, $updatedAt, $threadSource, $source, $isPinned, $sectionId, $preview);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$rollout", RolloutPath);
        command.Parameters.AddWithValue("$cwd", root);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$archived", archived ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", updatedAtMs);
        command.Parameters.AddWithValue("$threadSource", threadSource);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$isPinned", isPinned ? 1 : 0);
        command.Parameters.AddWithValue("$sectionId", (object?)sectionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$preview", preview);
        await command.ExecuteNonQueryAsync();
    }

    public async Task InsertSectionAsync(string id, string name)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO thread_sections (id, name) VALUES ($id, $name);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        await command.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(root, recursive: true);
        return ValueTask.CompletedTask;
    }
}
