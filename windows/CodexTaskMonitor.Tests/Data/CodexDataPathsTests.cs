using CodexTaskMonitor.Core.Data;

namespace CodexTaskMonitor.Tests.Data;

public sealed class CodexDataPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ForHome_RejectsInvalidHomeDirectory(string? homeDirectory)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            CodexDataPaths.ForHome(homeDirectory!, @"D:\LocalAppData"));

        Assert.Equal(nameof(homeDirectory), exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ForHome_RejectsInvalidLocalAppDataDirectory(string? localAppDataDirectory)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            CodexDataPaths.ForHome(@"C:\Users\Tester", localAppDataDirectory!));

        Assert.Equal(nameof(localAppDataDirectory), exception.ParamName);
    }

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
