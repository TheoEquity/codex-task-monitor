namespace CodexTaskMonitor.Core.Preferences;

public interface IMonitorPreferencesStore
{
    Task<MonitorPreferences> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(MonitorPreferences preferences, CancellationToken cancellationToken);
}
