using System.Text;
using System.Text.Json;
using CodexTaskMonitor.Core.Data;

namespace CodexTaskMonitor.Core.Monitoring;

public static class RolloutParser
{
    private static readonly string[] Markers = ["task_started", "task_complete", "turn_aborted"];

    public static async Task<LifecycleEvent?> LatestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var endsWithNewline = stream.Length == 0;
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            endsWithNewline = stream.ReadByte() == (byte)'\n';
            stream.Seek(0, SeekOrigin.Begin);
        }

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        LifecycleEvent? current = null;
        var pending = await reader.ReadLineAsync(cancellationToken);
        while (pending is not null)
        {
            var next = await reader.ReadLineAsync(cancellationToken);
            if (next is not null || endsWithNewline)
                current = ApplyLine(current, pending);

            pending = next;
        }

        return current;
    }

    public static LifecycleEvent? LatestAfter(LifecycleEvent? current, ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        var endsWithNewline = text.Length == 0 || text[^1] == '\n';
        var lines = text.Split('\n');
        var limit = endsWithNewline ? lines.Length : lines.Length - 1;
        for (var index = 0; index < limit; index++)
            current = ApplyLine(current, lines[index].TrimEnd('\r'));

        return current;
    }

    private static LifecycleEvent? ApplyLine(LifecycleEvent? current, string line)
    {
        if (string.IsNullOrEmpty(line) || !Markers.Any(line.Contains))
            return current;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var eventType) || eventType.GetString() != "event_msg")
                return current;

            var payload = root.GetProperty("payload");
            var kindText = payload.GetProperty("type").GetString();
            var kind = kindText switch
            {
                "task_started" => LifecycleKind.Started,
                "task_complete" => LifecycleKind.Completed,
                "turn_aborted" => LifecycleKind.Aborted,
                _ => (LifecycleKind?)null
            };
            if (kind is null)
                return current;

            var turnId = payload.GetProperty("turn_id").GetString();
            var started = payload.GetProperty("started_at").GetDouble();
            if (string.IsNullOrWhiteSpace(turnId))
                throw new JsonException("turn_id is missing");

            var startedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(started * 1000));
            if (kind == LifecycleKind.Started)
                return new LifecycleEvent(kind.Value, turnId, startedAt, null);

            var completed = payload.GetProperty("completed_at").GetDouble();
            var terminal = new LifecycleEvent(
                kind.Value,
                turnId,
                startedAt,
                DateTimeOffset.FromUnixTimeMilliseconds((long)(completed * 1000)));
            return current is null || current.TurnId == turnId ? terminal : current;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new CodexDataException(CodexDataError.FormatChanged, "Codex rollout format changed", error);
        }
    }
}
