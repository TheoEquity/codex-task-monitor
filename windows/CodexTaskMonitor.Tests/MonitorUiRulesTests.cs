using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class MonitorUiRulesTests
{
    [Theory]
    [InlineData(3, false, 237)]
    [InlineData(8, true, 458)]
    public void Height_MatchesApprovedLayout(int count, bool error, double expected) =>
        Assert.Equal(expected, MonitorPanelLayout.Height(count, error));

    [Fact]
    public void InsertedId_ReturnsOnlyAnUnambiguousAddition()
    {
        Assert.Equal("new", MonitorListUpdate.InsertedId(["first", "last"], ["first", "new", "last"]));
        Assert.Null(MonitorListUpdate.InsertedId(["first", "handled", "last"], ["new", "first", "last"]));
    }
}
