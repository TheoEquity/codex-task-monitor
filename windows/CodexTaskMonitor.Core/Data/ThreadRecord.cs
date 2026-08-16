using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Core.Data;

public sealed record ThreadRecord(
    string Id,
    string Title,
    string Cwd,
    DateTimeOffset UpdatedAt,
    string RolloutPath,
    ThreadGroupingInfo Grouping);

public enum CodexDataError
{
    DatabaseMissing,
    FormatChanged,
    Unreadable
}

public sealed class CodexDataException(CodexDataError error, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public CodexDataError Error { get; } = error;
}
