using System.Text;
using System.Text.Json;
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;

namespace CodexTaskMonitor.Tests.Monitoring;

public sealed class RolloutParserTests
{
    [Fact]
    public void CompleteTurn_ReturnsTerminalEvent()
    {
        var data = Encoding.UTF8.GetBytes("""
            {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-1","started_at":101}}
            {"type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1","started_at":101,"completed_at":102}}

            """);

        var item = RolloutParser.LatestAfter(null, data);

        Assert.Equal(LifecycleKind.Completed, item!.Kind);
        Assert.Equal("turn-1", item.TurnId);
    }

    [Fact]
    public void IncompleteTrailingJson_IsIgnored()
    {
        var data = Encoding.UTF8.GetBytes(
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\",\"started_at\":101}}\n{\"type\":");

        Assert.Equal(LifecycleKind.Started, RolloutParser.LatestAfter(null, data)!.Kind);
    }

    [Fact]
    public void NewlineTerminatedMalformedLifecycleLine_ReportsFormatChange()
    {
        var data = Encoding.UTF8.GetBytes("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"\n");

        var error = Assert.Throws<CodexDataException>(() => RolloutParser.LatestAfter(null, data));

        Assert.Equal(CodexDataError.FormatChanged, error.Error);
    }

    [Fact]
    public void LateTerminalForOldTurn_DoesNotReplaceNewerRunningTurn()
    {
        var current = new LifecycleEvent(LifecycleKind.Started, "turn-b", DateTimeOffset.FromUnixTimeSeconds(102), null);
        var appended = Encoding.UTF8.GetBytes(
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-a\",\"started_at\":101,\"completed_at\":103}}\n");

        Assert.Equal(current, RolloutParser.LatestAfter(current, appended));
    }

    [Fact]
    public async Task FileStream_ReturnsLatestAbortedTurnAndIgnoresUnrelatedRows()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
                {"type":"turn_context","payload":{"type":"task_started is text only"}}
                {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-a","started_at":101}}
                {"type":"response_item","payload":{"type":"message"}}
                {"type":"event_msg","payload":{"type":"turn_aborted","turn_id":"turn-a","started_at":101,"completed_at":102}}

                """);

            Assert.Equal(LifecycleKind.Aborted, (await RolloutParser.LatestAsync(path, default))!.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FileStream_IgnoresUnterminatedLifecycleTail()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path,
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-a\",\"started_at\":101}}\n" +
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"");

            Assert.Equal(LifecycleKind.Started, (await RolloutParser.LatestAsync(path, default))!.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NewerStartedTurn_WinsOverLateOldTerminal()
    {
        var data = Encoding.UTF8.GetBytes("""
            {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-a","started_at":101}}
            {"type":"event_msg","payload":{"type":"task_started","turn_id":"turn-b","started_at":102}}
            {"type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-a","started_at":101,"completed_at":103}}

            """);

        Assert.Equal("turn-b", RolloutParser.LatestAfter(null, data)!.TurnId);
    }

    [Fact]
    public void UnrelatedLineLargerThanTwoChunks_DoesNotTruncateLifecycleSearch()
    {
        var started = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\",\"started_at\":101}}\n";
        var unrelated = JsonSerializer.Serialize(new { type = "agent_reasoning", payload = new string('x', 130 * 1024) }) + "\n";
        var data = Encoding.UTF8.GetBytes(started + unrelated);

        Assert.Equal("turn-1", RolloutParser.LatestAfter(null, data)!.TurnId);
    }

    [Fact]
    public async Task FileStream_UsesStartingLengthSnapshotWhenWriterAppendsDuringScan()
    {
        var path = Path.GetTempFileName();
        try
        {
            var started = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-a\",\"started_at\":101}}\n";
            var unrelated = "{\"type\":\"response_item\",\"payload\":\"" + new string('x', 32 * 1024 * 1024) + "\"}\n";
            await File.WriteAllTextAsync(path, started + unrelated);

            var reading = RolloutParser.LatestAsync(path, default);
            Assert.False(reading.IsCompleted);
            await File.AppendAllTextAsync(path,
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-a\",\"started_at\":101,\"completed_at\":102}}\n");

            Assert.Equal(LifecycleKind.Started, (await reading)!.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LatestAfter_InvalidUtf8InCompleteLine_ReportsFormatChange()
    {
        var data = new byte[]
        {
            (byte)'{', (byte)'"', (byte)'t', (byte)'y', (byte)'p', (byte)'e', (byte)'"', (byte)':',
            (byte)'"', (byte)'e', (byte)'v', (byte)'e', (byte)'n', (byte)'t', (byte)'_', (byte)'m', (byte)'s', (byte)'g', (byte)'"',
            (byte)',', 0xFF, (byte)'}', (byte)'\n'
        };

        var error = Assert.Throws<CodexDataException>(() => RolloutParser.LatestAfter(null, data));

        Assert.Equal(CodexDataError.FormatChanged, error.Error);
        Assert.DoesNotContain("event_msg", error.Message);
    }

    [Fact]
    public async Task FileStream_InvalidUtf8InCompleteLine_ReportsFormatChange()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [
                (byte)'{', (byte)'"', (byte)'t', (byte)'y', (byte)'p', (byte)'e', (byte)'"', (byte)':',
                (byte)'"', (byte)'e', (byte)'v', (byte)'e', (byte)'n', (byte)'t', (byte)'_', (byte)'m', (byte)'s', (byte)'g', (byte)'"',
                (byte)',', 0xFF, (byte)'}', (byte)'\n'
            ]);

            var error = await Assert.ThrowsAsync<CodexDataException>(() => RolloutParser.LatestAsync(path, default));

            Assert.Equal(CodexDataError.FormatChanged, error.Error);
            Assert.DoesNotContain("event_msg", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utf8Bom_IsConsistentlySupported(bool fromFile)
    {
        var data = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-a\",\"started_at\":101}}\n"))
            .ToArray();

        if (!fromFile)
        {
            Assert.Equal(LifecycleKind.Started, RolloutParser.LatestAfter(null, data)!.Kind);
            return;
        }

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, data);
            Assert.Equal(LifecycleKind.Started, (await RolloutParser.LatestAsync(path, default))!.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utf8BomAndCrLf_PreserveMultibyteTurnId(bool fromFile)
    {
        const string turnId = "turn-\u00e9-\u4e00";
        var json = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"" + turnId + "\",\"started_at\":101}}\r\n";
        var data = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(json))
            .ToArray();

        if (!fromFile)
        {
            Assert.Equal(turnId, RolloutParser.LatestAfter(null, data)!.TurnId);
            return;
        }

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, data);
            Assert.Equal(turnId, (await RolloutParser.LatestAsync(path, default))!.TurnId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EscapedLifecycleType_IsRecognizedWithoutBroadeningSupportedTypes()
    {
        var data = Encoding.UTF8.GetBytes(
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_\\u0073tarted\",\"turn_id\":\"turn-a\",\"started_at\":101}}\n");

        Assert.Equal(LifecycleKind.Started, RolloutParser.LatestAfter(null, data)!.Kind);
    }

    [Fact]
    public void NewlineTerminatedMalformedEscapedLifecycleEnvelope_ReportsFormatChangeWithoutBody()
    {
        var data = Encoding.UTF8.GetBytes(
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_\\u0073tarted\"\n");

        var error = Assert.Throws<CodexDataException>(() => RolloutParser.LatestAfter(null, data));

        Assert.Equal(CodexDataError.FormatChanged, error.Error);
        Assert.DoesNotContain("event_msg", error.Message);
        Assert.DoesNotContain("task_", error.Message);
    }

    [Fact]
    public void NewlineTerminatedMalformedUnrelatedLine_IsIgnored()
    {
        var data = Encoding.UTF8.GetBytes("{\"type\":\"response_item\"\n");

        Assert.Null(RolloutParser.LatestAfter(null, data));
    }

    [Fact]
    public void UnrelatedEventContainingLifecycleMarker_IsIgnored()
    {
        var data = Encoding.UTF8.GetBytes(
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"message\",\"text\":\"task_started\"}}\n");

        Assert.Null(RolloutParser.LatestAfter(null, data));
    }
}
