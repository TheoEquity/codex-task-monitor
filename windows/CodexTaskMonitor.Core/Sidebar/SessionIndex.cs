using System.Text.Json;

namespace CodexTaskMonitor.Core.Sidebar;

public static class SessionIndex
{
    public static string? LatestTitle(string threadId, ReadOnlySpan<byte> data)
    {
        string? title = null;
        var endsWithNewline = data.Length == 0 || data[^1] == (byte)'\n';
        var lines = data.ToArray().AsSpan();
        var start = 0;

        for (var index = 0; index <= lines.Length; index++)
        {
            if (index < lines.Length && lines[index] != (byte)'\n') continue;

            var line = lines[start..index];
            start = index + 1;
            if (index == lines.Length && !endsWithNewline) break;
            if (line.IsEmpty) continue;

            using var document = ParseCompleteRecord(line);
            var root = document.RootElement;
            if (root.GetProperty("id").GetString() != threadId) continue;

            var candidate = root.GetProperty("thread_name").GetString();
            title = string.IsNullOrEmpty(candidate) ? null : candidate;
        }

        return title;
    }

    private static JsonDocument ParseCompleteRecord(ReadOnlySpan<byte> line)
    {
        try
        {
            return JsonDocument.Parse(line.ToArray());
        }
        catch (JsonException exception)
        {
            throw new JsonException("A complete session-index record is malformed.", exception);
        }
    }
}
