using System.Text;
using System.Text.Json;
using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Tests.Sidebar;

public sealed class SidebarTargetResolverTests
{
    private const string ThreadId = "11111111-1111-4111-8111-111111111111";

    [Fact]
    public void LatestCompleteSessionRow_WinsOverOlderTitleAndPartialTail()
    {
        var data = Encoding.UTF8.GetBytes(
            $$"""
            {"id":"{{ThreadId}}","thread_name":"Old"}
            {"id":"{{ThreadId}}","thread_name":"Sidebar title"}
            {"id":"other","thread_name":
            """);

        Assert.Equal("Sidebar title", SessionIndex.LatestTitle(ThreadId, data));
    }

    [Fact]
    public void PinnedDatabaseFlag_HasHighestPriority()
    {
        var target = SidebarTargetResolver.Resolve(
            ThreadId,
            new ThreadGroupingInfo(true, "Research", @"C:\work\demo"),
            TitleData("Pinned task"),
            ProjectState("Assigned"));

        Assert.Equal(new SidebarTarget("Pinned task", SidebarThreadGroup.Pinned()), target);
    }

    [Fact]
    public void SectionName_WinsBeforeProjectFallback()
    {
        var target = SidebarTargetResolver.Resolve(
            ThreadId,
            new ThreadGroupingInfo(false, "Research", @"C:\work\demo"),
            TitleData("Section task"),
            ProjectState("Assigned"));

        Assert.Equal(new SidebarTarget("Section task", SidebarThreadGroup.Section("Research")), target);
    }

    [Fact]
    public void DirectProjectAssignment_WinsBeforeCwdFallback()
    {
        var target = SidebarTargetResolver.Resolve(
            ThreadId,
            new ThreadGroupingInfo(false, null, @"C:\work\demo"),
            TitleData("Assigned task"),
            ProjectState("Assigned"));

        Assert.Equal(new SidebarTarget("Assigned task", SidebarThreadGroup.Project("Assigned")), target);
    }

    [Fact]
    public void ProjectlessMembership_WinsBeforeCwdFallback()
    {
        var target = SidebarTargetResolver.Resolve(
            ThreadId,
            new ThreadGroupingInfo(false, null, @"C:\work\demo"),
            TitleData("Projectless task"),
            ProjectlessState());

        Assert.Equal(new SidebarTarget("Projectless task", SidebarThreadGroup.Projectless()), target);
    }

    [Theory]
    [InlineData(true, "DemoProject")]
    [InlineData(false, null)]
    public void ProjectFallback_IsAcceptedOnlyWhenUnique(bool unique, string? expected)
    {
        var duplicate = unique ? "" : ",\"p2\":{\"name\":\"Other\",\"rootPaths\":[\"C:\\\\work\\\\demo\"]}";
        var state = Encoding.UTF8.GetBytes(
            "{\"local-projects\":{\"p1\":{\"name\":\"DemoProject\",\"rootPaths\":[\"C:\\\\work\\\\demo\"]}" + duplicate + "}}");
        var target = SidebarTargetResolver.Resolve(
            ThreadId,
            new ThreadGroupingInfo(false, null, @"C:\work\demo"),
            TitleData("Task"),
            state);

        Assert.Equal(expected, target?.Group.Name);
    }

    [Fact]
    public void ProjectFallback_RejectsTwoMatchingProjectsWithTheSameName()
    {
        var state = Encoding.UTF8.GetBytes(
            "{\"local-projects\":{\"p1\":{\"name\":\"DemoProject\",\"rootPaths\":[\"C:\\\\work\\\\demo\"]},\"p2\":{\"name\":\"DemoProject\",\"rootPaths\":[\"C:\\\\work\\\\demo\"]}}}");

        var target = SidebarTargetResolver.Resolve(
            ThreadId,
            new ThreadGroupingInfo(false, null, @"C:\work\demo"),
            TitleData("Task"),
            state);

        Assert.Null(target);
    }

    [Fact]
    public void MissingExactTitle_ReturnsNull()
    {
        var grouping = new ThreadGroupingInfo(false, null, @"C:\work\demo");

        Assert.Null(SidebarTargetResolver.Resolve(ThreadId, grouping, ""u8.ToArray(), "{}"u8.ToArray()));
    }

    [Fact]
    public void SessionIndex_UsesOnlyNewlineTerminatedRecords()
    {
        var complete = JsonSerializer.Serialize(new { id = ThreadId, thread_name = "Complete" }) + "\n";
        var validButUnterminated = JsonSerializer.Serialize(new { id = ThreadId, thread_name = "Do not use yet" });

        Assert.Equal("Complete", SessionIndex.LatestTitle(ThreadId, Encoding.UTF8.GetBytes(complete + validButUnterminated)));
        Assert.Equal("Complete", SessionIndex.LatestTitle(ThreadId, Encoding.UTF8.GetBytes(complete + "{\"id\":")));
        Assert.Throws<JsonException>(() => SessionIndex.LatestTitle(
            ThreadId, Encoding.UTF8.GetBytes(complete + "{\"id\":}\n")));
    }

    private static byte[] TitleData(string title) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = ThreadId, thread_name = title }) + "\n");

    private static byte[] ProjectState(string projectName) =>
        Encoding.UTF8.GetBytes(
            "{\"thread-project-assignments\":{\"" + ThreadId + "\":{\"projectId\":\"p1\"}},\"local-projects\":{\"p1\":{\"name\":\"" + projectName + "\",\"rootPaths\":[\"C:\\\\work\\\\demo\"]}}}");

    private static byte[] ProjectlessState() =>
        Encoding.UTF8.GetBytes(
            "{\"projectless-thread-ids\":[\"" + ThreadId + "\"],\"local-projects\":{\"p1\":{\"name\":\"Fallback\",\"rootPaths\":[\"C:\\\\work\\\\demo\"]}}}");
}
