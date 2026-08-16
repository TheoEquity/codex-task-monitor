using CodexTaskMonitor.Core.Data;

namespace CodexTaskMonitor.Tests.Data;

public sealed class CodexDataPathsTests
{
    [Fact]
    public void ForHome_UsesCodexAndLocalAppDataRoots()
    {
        var paths = CodexDataPaths.ForHome(@"C:\Users\Tester", @"D:\LocalAppData");

        Assert.Equal(@"C:\Users\Tester\.codex\state_5.sqlite", paths.DatabasePath);
        Assert.Equal(@"C:\Users\Tester\.codex\session_index.jsonl", paths.SessionIndexPath);
        Assert.Equal(@"C:\Users\Tester\.codex\.codex-global-state.json", paths.GlobalStatePath);
        Assert.Equal(@"D:\LocalAppData\CodexTaskMonitor\settings.json", paths.PreferencesPath);
        Assert.Equal(@"D:\LocalAppData\CodexTaskMonitor\logs", paths.LogDirectory);
    }
}
