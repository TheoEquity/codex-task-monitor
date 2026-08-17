using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace CodexTaskMonitor.Windows.Notifications;

public interface IBarkStateStore
{
    Task<BarkState> LoadAsync(CancellationToken token);
    Task SaveAsync(BarkState state, CancellationToken token);
    Task<bool> MarkNotifiedAsync(Guid expectedConfigurationId, string itemId, CancellationToken token);
}

public sealed class BarkStateStore(string path) : IBarkStateStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathGates = new(StringComparer.OrdinalIgnoreCase);

    public async Task<BarkState> LoadAsync(CancellationToken token)
    {
        var gate = GateFor(path);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(BarkState state, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);
        var gate = GateFor(path);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(state, token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> MarkNotifiedAsync(
        Guid expectedConfigurationId,
        string itemId,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        var gate = GateFor(path);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(token).ConfigureAwait(false);
            if (!current.Enabled || current.ConfigurationId != expectedConfigurationId)
                return false;

            if (!current.NotifiedItemIds.Contains(itemId))
                await SaveCoreAsync(current.MarkNotified(itemId), token).ConfigureAwait(false);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BarkState> LoadCoreAsync(CancellationToken token)
    {
        if (!File.Exists(path))
            return BarkState.Disabled;

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<BarkStateDocument>(stream, Options, token).ConfigureAwait(false);
        return document?.ToState() ?? BarkState.Disabled;
    }

    private async Task SaveCoreAsync(BarkState state, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("A Bark state path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream, BarkStateDocument.From(state), Options, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static SemaphoreSlim GateFor(string filePath) =>
        PathGates.GetOrAdd(Path.GetFullPath(filePath), static _ => new SemaphoreSlim(1, 1));

    private sealed record BarkStateDocument(
        bool Enabled,
        Guid ConfigurationId,
        DateTimeOffset? EnabledAt,
        string[]? NotifiedItemIds)
    {
        public static BarkStateDocument From(BarkState state) =>
            new(state.Enabled, state.ConfigurationId, state.EnabledAt, state.NotifiedItemIds.ToArray());

        public BarkState ToState() =>
            new(Enabled, ConfigurationId, EnabledAt, NotifiedItemIds ?? []);
    }
}
