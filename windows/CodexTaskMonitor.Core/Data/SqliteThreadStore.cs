using CodexTaskMonitor.Core.Sidebar;
using Microsoft.Data.Sqlite;

namespace CodexTaskMonitor.Core.Data;

public sealed class SqliteThreadStore(string databasePath) : IThreadStore, IThreadGroupingLookup
{
    private static readonly string[] RequiredThreadColumns =
    [
        "id", "rollout_path", "cwd", "title", "archived", "updated_at_ms",
        "thread_source", "source", "preview", "is_pinned", "thread_section_id"
    ];

    private static readonly string[] RequiredSectionColumns = ["id", "name"];

    public async Task<IReadOnlyList<ThreadRecord>> ReadThreadsAsync(
        DateTimeOffset updatedAfter,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenReadOnlyConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT t.id, t.title, t.cwd, t.updated_at_ms, t.rollout_path,
                       t.is_pinned, s.name
                FROM threads t
                LEFT JOIN thread_sections s ON s.id = t.thread_section_id
                WHERE t.archived = 0
                  AND t.preview <> ''
                  AND (COALESCE(t.thread_source, 'user') <> 'subagent' OR t.source = 'vscode')
                  AND t.updated_at_ms >= $updatedAfter
                ORDER BY t.updated_at_ms DESC;
                """;
            command.Parameters.AddWithValue("$updatedAfter", updatedAfter.ToUnixTimeMilliseconds());

            var records = new List<ThreadRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var cwd = reader.GetString(2);
                records.Add(new ThreadRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    cwd,
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                    reader.GetString(4),
                    new ThreadGroupingInfo(
                        reader.GetInt64(5) != 0,
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        cwd)));
            }

            return records;
        }
        catch (CodexDataException)
        {
            throw;
        }
        catch (SqliteException error)
        {
            throw new CodexDataException(CodexDataError.Unreadable, "Unable to read Codex database", error);
        }
    }

    public async Task<ThreadGroupingInfo?> FindGroupingAsync(string threadId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenReadOnlyConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT t.is_pinned, s.name, t.cwd
                FROM threads t
                LEFT JOIN thread_sections s ON s.id = t.thread_section_id
                WHERE t.id = $threadId;
                """;
            command.Parameters.AddWithValue("$threadId", threadId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new ThreadGroupingInfo(
                    reader.GetInt64(0) != 0,
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2))
                : null;
        }
        catch (CodexDataException)
        {
            throw;
        }
        catch (SqliteException error)
        {
            throw new CodexDataException(CodexDataError.Unreadable, "Unable to read Codex database", error);
        }
    }

    private async Task<SqliteConnection> OpenReadOnlyConnectionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
            throw new CodexDataException(CodexDataError.DatabaseMissing, "Codex database is missing");

        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await EnsureColumnsAsync(connection, "threads", RequiredThreadColumns, cancellationToken);
        await EnsureColumnsAsync(connection, "thread_sections", RequiredSectionColumns, cancellationToken);
    }

    private static async Task EnsureColumnsAsync(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> required,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        var actual = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            actual.Add(reader.GetString(1));

        if (required.Any(column => !actual.Contains(column)))
            throw new CodexDataException(CodexDataError.FormatChanged, $"Codex {table} schema changed");
    }
}
