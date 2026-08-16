namespace CodexTaskMonitor.Core.Data;

public sealed record CodexDataPaths(
    string DatabasePath,
    string SessionIndexPath,
    string GlobalStatePath,
    string PreferencesPath,
    string LogDirectory)
{
    public static CodexDataPaths ForHome(string homeDirectory, string localAppDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataDirectory);

        var codex = Path.Combine(homeDirectory, ".codex");
        var app = Path.Combine(localAppDataDirectory, "CodexTaskMonitor");
        return new CodexDataPaths(
            Path.Combine(codex, "state_5.sqlite"),
            Path.Combine(codex, "session_index.jsonl"),
            Path.Combine(codex, ".codex-global-state.json"),
            Path.Combine(app, "settings.json"),
            Path.Combine(app, "logs"));
    }
}
