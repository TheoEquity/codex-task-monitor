using System.Collections.Concurrent;
using System.Text.Json;

namespace CodexTaskMonitor.Core.Preferences;

public sealed class MonitorPreferencesStore(string path) : IMonitorPreferencesStore
{
    private const int FileOperationAttempts = 4;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathGates = new(StringComparer.OrdinalIgnoreCase);

    public async Task<MonitorPreferences> LoadAsync(CancellationToken cancellationToken)
    {
        var pathGate = GateFor(path);
        await pathGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
                return MonitorPreferences.Empty;

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<MonitorPreferencesDocument>(stream, Options, cancellationToken);
            return document?.ToPreferences() ?? MonitorPreferences.Empty;
        }
        finally
        {
            pathGate.Release();
        }
    }

    public async Task SaveAsync(MonitorPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A preferences path must include a directory.", nameof(path));
        var saveGate = GateFor(path);
        await saveGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);

            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            Exception? saveFailure = null;
            try
            {
                await using (var stream = new FileStream(
                    temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, MonitorPreferencesDocument.From(preferences), Options, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                MoveTemporaryIntoPlace(temporary, path, cancellationToken);
            }
            catch (Exception error)
            {
                saveFailure = error;
                throw;
            }
            finally
            {
                try
                {
                    DeleteTemporary(temporary);
                }
                catch when (saveFailure is not null)
                {
                    // Preserve the serialization or replace failure; cleanup is best effort after an aborted save.
                }
            }
        }
        finally
        {
            saveGate.Release();
        }
    }

    private static SemaphoreSlim GateFor(string filePath) =>
        PathGates.GetOrAdd(Path.GetFullPath(filePath), static _ => new SemaphoreSlim(1, 1));

    private static void DeleteTemporary(string temporary)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
                return;
            }
            catch (IOException) when (attempt < FileOperationAttempts - 1)
            {
                Thread.Sleep(20);
            }
            catch (UnauthorizedAccessException) when (attempt < FileOperationAttempts - 1)
            {
                Thread.Sleep(20);
            }
        }
    }

    private static void MoveTemporaryIntoPlace(string temporary, string destination, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporary, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < FileOperationAttempts - 1)
            {
                Thread.Sleep(20);
            }
            catch (UnauthorizedAccessException) when (attempt < FileOperationAttempts - 1)
            {
                Thread.Sleep(20);
            }
        }
    }

    private sealed record MonitorPreferencesDocument(
        DateTimeOffset? Baseline,
        string[]? AdoptedTurnIds,
        string[]? DismissedTurnIds,
        string[]? DismissedItemIds,
        double? WindowLeft,
        double? WindowTop,
        bool? LaunchAtLoginEnabled)
    {
        public static MonitorPreferencesDocument From(MonitorPreferences preferences) =>
            new(
                preferences.Baseline,
                preferences.AdoptedTurnIds.ToArray(),
                preferences.DismissedTurnIds.ToArray(),
                preferences.DismissedItemIds.ToArray(),
                preferences.WindowLeft,
                preferences.WindowTop,
                preferences.LaunchAtLoginEnabled);

        public MonitorPreferences ToPreferences() =>
            CreatePreferences();

        private MonitorPreferences CreatePreferences()
        {
            var dismissedItemIds = DismissedItemIds ?? [];
            if (dismissedItemIds.Any(itemId => !MonitorPreferences.IsExactHandledItemKey(itemId)))
                throw new InvalidDataException("The preferences file contains an invalid handled-item identifier.");

            return new MonitorPreferences(
                Baseline,
                AdoptedTurnIds ?? [],
                DismissedTurnIds ?? [],
                dismissedItemIds,
                WindowLeft,
                WindowTop,
                LaunchAtLoginEnabled);
        }
    }
}
