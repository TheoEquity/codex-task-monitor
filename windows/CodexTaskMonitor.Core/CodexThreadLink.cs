namespace CodexTaskMonitor.Core;

public static class CodexThreadLink
{
    public static bool TryCreate(string threadId, out Uri? uri)
    {
        uri = null;
        if (!Guid.TryParseExact(threadId, "D", out var parsed)) return false;
        uri = new Uri($"codex://threads/{parsed:D}");
        return true;
    }
}
