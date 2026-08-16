using System.Buffers;
using System.Text;
using System.Text.Json;
using CodexTaskMonitor.Core.Data;

namespace CodexTaskMonitor.Core.Monitoring;

public static class RolloutParser
{
    private const int BufferSize = 81920;
    private static readonly string[] Markers = ["task_started", "task_complete", "turn_aborted"];
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<LifecycleEvent?> LatestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var remaining = stream.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var line = new ArrayBufferWriter<byte>();
        var isFirstLine = true;
        LifecycleEvent? current = null;

        try
        {
            while (remaining > 0)
            {
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0)
                    break;

                remaining -= read;
                var unread = buffer.AsMemory(0, read);
                while (!unread.IsEmpty)
                {
                    var newline = unread.Span.IndexOf((byte)'\n');
                    if (newline < 0)
                    {
                        line.Write(unread.Span);
                        break;
                    }

                    var completeLine = unread[..(newline + 1)];
                    line.Write(completeLine.Span);
                    current = ApplyCompleteLine(current, line.WrittenSpan, isFirstLine);
                    isFirstLine = false;
                    line.Clear();
                    unread = unread[(newline + 1)..];
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return current;
    }

    public static LifecycleEvent? LatestAfter(LifecycleEvent? current, ReadOnlySpan<byte> data)
    {
        var lastNewline = data.LastIndexOf((byte)'\n');
        if (lastNewline < 0)
            return current;

        var text = DecodeCompleteUtf8(data[..(lastNewline + 1)], isFirstLine: true);
        var lines = text.Split('\n');
        foreach (var line in lines)
            current = ApplyLine(current, line.TrimEnd('\r'));

        return current;
    }

    private static LifecycleEvent? ApplyCompleteLine(
        LifecycleEvent? current,
        ReadOnlySpan<byte> data,
        bool isFirstLine) =>
        ApplyLine(current, DecodeCompleteUtf8(data, isFirstLine).TrimEnd('\n').TrimEnd('\r'));

    private static string DecodeCompleteUtf8(ReadOnlySpan<byte> data, bool isFirstLine)
    {
        try
        {
            var text = StrictUtf8.GetString(data);
            return isFirstLine && text.StartsWith('\uFEFF') ? text[1..] : text;
        }
        catch (DecoderFallbackException error)
        {
            throw FormatChanged(error);
        }
    }

    private static LifecycleEvent? ApplyLine(LifecycleEvent? current, string line)
    {
        if (string.IsNullOrEmpty(line))
            return current;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var eventType) ||
                eventType.ValueKind != JsonValueKind.String ||
                eventType.GetString() != "event_msg" ||
                !root.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("type", out var kindElement) ||
                kindElement.ValueKind != JsonValueKind.String)
                return current;

            var kind = kindElement.GetString() switch
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
        catch (JsonException) when (!ContainsLifecycleMarker(line))
        {
            return current;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException or OverflowException)
        {
            throw FormatChanged(error);
        }
    }

    private static CodexDataException FormatChanged(Exception error) =>
        new(CodexDataError.FormatChanged, "Codex rollout format changed", error);

    private static bool ContainsLifecycleMarker(string line) =>
        line.Contains("\"event_msg\"", StringComparison.Ordinal) &&
        Markers.Any(marker => ContainsJsonEscapedAscii(line, marker));

    private static bool ContainsJsonEscapedAscii(string text, string marker)
    {
        for (var start = 0; start < text.Length; start++)
        {
            var index = start;
            var markerIndex = 0;
            while (markerIndex < marker.Length && index < text.Length)
            {
                if (text[index] == marker[markerIndex])
                {
                    index++;
                    markerIndex++;
                    continue;
                }

                if (text[index] != '\\' ||
                    index + 5 >= text.Length ||
                    text[index + 1] != 'u' ||
                    !TryReadHex(text.AsSpan(index + 2, 4), out var value) ||
                    value != marker[markerIndex])
                    break;

                index += 6;
                markerIndex++;
            }

            if (markerIndex == marker.Length)
                return true;
        }

        return false;
    }

    private static bool TryReadHex(ReadOnlySpan<char> text, out char value)
    {
        value = default;
        var number = 0;
        foreach (var character in text)
        {
            var digit = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                >= 'A' and <= 'F' => character - 'A' + 10,
                _ => -1
            };
            if (digit < 0)
                return false;

            number = (number * 16) + digit;
        }

        value = (char)number;
        return true;
    }
}
