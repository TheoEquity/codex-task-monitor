using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class CodexThreadLinkTests
{
    [Fact]
    public void ValidUuid_BuildsCodexThreadUri()
    {
        Assert.True(CodexThreadLink.TryCreate("11111111-1111-4111-8111-111111111111", out var uri));
        Assert.Equal("codex://threads/11111111-1111-4111-8111-111111111111", uri!.AbsoluteUri);
    }

    [Fact]
    public void InvalidUuid_IsRejected() => Assert.False(CodexThreadLink.TryCreate("not-a-uuid", out _));
}
