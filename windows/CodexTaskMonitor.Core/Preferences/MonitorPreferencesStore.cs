using System.Text.Json;

namespace CodexTaskMonitor.Core.Preferences;

public sealed class MonitorPreferencesStore(string path) : IMonitorPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<MonitorPreferences> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return MonitorPreferences.Empty;

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<MonitorPreferencesDocument>(stream, Options, cancellationToken);
        return document?.ToPreferences() ?? MonitorPreferences.Empty;
    }

    public async Task SaveAsync(MonitorPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A preferences path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, MonitorPreferencesDocument.From(preferences), Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
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
            new(
                Baseline,
                AdoptedTurnIds ?? [],
                DismissedTurnIds ?? [],
                DismissedItemIds ?? [],
                WindowLeft,
                WindowTop,
                LaunchAtLoginEnabled);
    }
}
