using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Core.Data;

public interface IThreadStore
{
    Task<IReadOnlyList<ThreadRecord>> ReadThreadsAsync(DateTimeOffset updatedAfter, CancellationToken cancellationToken);
}

public interface IThreadGroupingLookup
{
    Task<ThreadGroupingInfo?> FindGroupingAsync(string threadId, CancellationToken cancellationToken);
}
