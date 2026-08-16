using CodexTaskMonitor.Core.Data;

namespace CodexTaskMonitor.Core.Monitoring;

public sealed class TaskMonitor(IThreadStore threadStore) : ITaskMonitor
{
    private const int TailReadBufferSize = 64 * 1024;
    private readonly Dictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlySet<string>> CurrentlyRunningTurnIdsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var (events, unreadable) = await LatestEventsAsync(since, cancellationToken);
        if (unreadable != 0)
            throw new CodexDataException(CodexDataError.Unreadable, "Some rollouts are unreadable");

        return events.Values
            .Where(item => item.Event.Kind == LifecycleKind.Started)
            .Select(item => item.Event.TurnId)
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<MonitorScanResult> ScanAsync(MonitorScanOptions options, CancellationToken cancellationToken)
    {
        var (events, unreadable) = await LatestEventsAsync(options.Baseline.AddHours(-1), cancellationToken);
        var items = events.Values
            .Select(pair => ToMonitorItem(pair, options))
            .OfType<MonitorItem>()
            .OrderByDescending(item => item.EventDate)
            .ToArray();

        return new MonitorScanResult(items, unreadable);
    }

    private static MonitorItem? ToMonitorItem(ThreadEvent pair, MonitorScanOptions options)
    {
        var state = TaskStateResolver.Resolve(
            pair.Event,
            options.Baseline,
            options.AdoptedTurnIds,
            options.DismissedTurnIds);
        if (state is null)
            return null;

        var projectName = Path.GetFileName(pair.Thread.Cwd.TrimEnd(Path.DirectorySeparatorChar));
        var item = new MonitorItem(
            pair.Thread.Id,
            pair.Event.TurnId,
            string.IsNullOrEmpty(pair.Thread.Title) ? "New chat" : pair.Thread.Title,
            pair.Thread.Cwd,
            projectName,
            pair.Event.ActivityDate,
            state.Value);
        return options.DismissedItemIds.Contains(item.Id) ? null : item;
    }

    private async Task<(Dictionary<string, ThreadEvent> Events, int Unreadable)> LatestEventsAsync(
        DateTimeOffset updatedAfter,
        CancellationToken cancellationToken)
    {
        var events = new Dictionary<string, ThreadEvent>(StringComparer.Ordinal);
        var unreadable = 0;
        var threads = await threadStore.ReadThreadsAsync(updatedAfter, cancellationToken);

        foreach (var thread in threads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var lifecycleEvent = await EventForAsync(thread.RolloutPath, cancellationToken);
                if (lifecycleEvent is not null)
                    events[thread.Id] = new ThreadEvent(thread, lifecycleEvent);
            }
            catch (CodexDataException error) when (error.Error != CodexDataError.FormatChanged)
            {
                unreadable++;
                AddCachedEvent(thread, events);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                unreadable++;
                AddCachedEvent(thread, events);
            }
        }

        if (events.Count == 0 && unreadable > 0)
            throw new CodexDataException(CodexDataError.Unreadable, "All relevant rollouts are unreadable");

        return (events, unreadable);
    }

    private void AddCachedEvent(ThreadRecord thread, Dictionary<string, ThreadEvent> events)
    {
        if (cache.TryGetValue(thread.RolloutPath, out var cached) && cached.Event is not null)
            events[thread.Id] = new ThreadEvent(thread, cached.Event);
    }

    private async Task<LifecycleEvent?> EventForAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists)
            throw new CodexDataException(CodexDataError.Unreadable, "Rollout is missing");

        var signature = FileSignature.From(info);
        if (cache.TryGetValue(path, out var cached) && cached.Signature == signature)
            return cached.Event;

        LifecycleEvent? lifecycleEvent;
        long processedSize;
        byte[] trailingFragment;
        if (cached is not null && signature.IsContinuationOf(cached.Signature) && info.Length > cached.SnapshotSize)
        {
            var appended = await ReadRangeAsync(path, cached.SnapshotSize, info.Length, cancellationToken);
            var combined = new byte[checked(cached.TrailingFragment.Length + appended.Length)];
            cached.TrailingFragment.CopyTo(combined, 0);
            appended.CopyTo(combined, cached.TrailingFragment.Length);
            lifecycleEvent = RolloutParser.LatestAfter(cached.Event, combined);
            var lastNewline = Array.LastIndexOf(combined, (byte)'\n');
            processedSize = lastNewline < 0 ? cached.ProcessedSize : cached.ProcessedSize + lastNewline + 1L;
            trailingFragment = TrailingFragment(combined, lastNewline);
        }
        else
        {
            lifecycleEvent = await RolloutParser.LatestAsync(path, cancellationToken);
            trailingFragment = await ReadTrailingFragmentAsync(path, info.Length, cancellationToken);
            processedSize = info.Length - trailingFragment.Length;
        }

        cache[path] = new CacheEntry(signature, lifecycleEvent, info.Length, processedSize, trailingFragment);
        return lifecycleEvent;
    }

    private static byte[] TrailingFragment(byte[] data, int lastNewline) =>
        lastNewline < 0 ? data : data[(lastNewline + 1)..];

    private static async Task<byte[]> ReadRangeAsync(
        string path,
        long start,
        long end,
        CancellationToken cancellationToken)
    {
        var length = checked((int)(end - start));
        await using var stream = OpenRead(path);
        if (stream.Length < end)
            throw new IOException("Rollout changed while reading");

        stream.Seek(start, SeekOrigin.Begin);
        var result = new byte[length];
        await stream.ReadExactlyAsync(result, cancellationToken);
        return result;
    }

    private static async Task<byte[]> ReadTrailingFragmentAsync(
        string path,
        long snapshotSize,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(path);
        if (stream.Length < snapshotSize)
            throw new IOException("Rollout changed while reading");

        var offset = snapshotSize;
        var laterChunks = new List<byte[]>();
        while (offset > 0)
        {
            var count = (int)Math.Min(TailReadBufferSize, offset);
            offset -= count;
            stream.Seek(offset, SeekOrigin.Begin);
            var chunk = new byte[count];
            await stream.ReadExactlyAsync(chunk, cancellationToken);
            var newline = Array.LastIndexOf(chunk, (byte)'\n');
            if (newline >= 0)
            {
                using var result = new MemoryStream();
                result.Write(chunk, newline + 1, chunk.Length - newline - 1);
                foreach (var later in laterChunks.AsEnumerable().Reverse())
                    result.Write(later);
                return result.ToArray();
            }

            laterChunks.Add(chunk);
        }

        using var complete = new MemoryStream();
        foreach (var chunk in laterChunks.AsEnumerable().Reverse())
            complete.Write(chunk);
        return complete.ToArray();
    }

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, TailReadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private sealed record CacheEntry(
        FileSignature Signature,
        LifecycleEvent? Event,
        long SnapshotSize,
        long ProcessedSize,
        byte[] TrailingFragment);

    private readonly record struct FileSignature(DateTime LastWriteTimeUtc, DateTime CreationTimeUtc, long Length)
    {
        public static FileSignature From(FileInfo info) => new(info.LastWriteTimeUtc, info.CreationTimeUtc, info.Length);

        public bool IsContinuationOf(FileSignature previous) =>
            CreationTimeUtc == previous.CreationTimeUtc && Length > previous.Length;
    }

    private sealed record ThreadEvent(ThreadRecord Thread, LifecycleEvent Event);
}
