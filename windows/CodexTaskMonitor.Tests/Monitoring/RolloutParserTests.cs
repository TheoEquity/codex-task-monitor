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
}
