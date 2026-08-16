using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Sidebar;
using System.IO;

namespace CodexTaskMonitor.Windows.Automation;

public interface IWindowsSidebarRevealer
{
    Task<string?> RevealAsync(MonitorItem item, CancellationToken token);
}

public sealed class WindowsSidebarRevealer(
    IChatGptWindowLocator windows,
    SidebarScrollController scroller,
    string sessionIndexPath,
    string globalStatePath,
    IThreadGroupingLookup groupingLookup,
    TimeProvider time,
    IUiAutomationRootReadinessProbe? rootReadiness = null) : IWindowsSidebarRevealer
{
    private static readonly TimeSpan WindowReadinessTimeout = TimeSpan.FromSeconds(5);
    private readonly IUiAutomationRootReadinessProbe rootReadiness = rootReadiness ?? new UiAutomationRootReadinessProbe();

    public async Task<string?> RevealAsync(MonitorItem item, CancellationToken token)
    {
        var deadline = time.GetTimestamp() +
            (long)(WindowReadinessTimeout.TotalSeconds * time.TimestampFrequency);
        nint handle = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var remainingTicks = deadline - time.GetTimestamp();
            if (remainingTicks <= 0)
                return "已打开对话；暂时无法在侧栏定位";

            handle = windows.FindMainWindow();
            if (handle != 0 && await IsUiAutomationRootReadyAsync(handle, remainingTicks, token).ConfigureAwait(false))
                break;

            var remaining = TimeSpan.FromSeconds((double)remainingTicks / time.TimestampFrequency);
            await Task.Delay(
                remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250),
                time,
                token).ConfigureAwait(false);
        }

        var resolution = await Task.Run(
            async () =>
            {
                var grouping = await groupingLookup.FindGroupingAsync(item.ThreadId, token).ConfigureAwait(false);
                if (grouping is null)
                    return new TargetResolution(false, null);

                var sessionIndex = await File.ReadAllBytesAsync(sessionIndexPath, token).ConfigureAwait(false);
                var globalState = await File.ReadAllBytesAsync(globalStatePath, token).ConfigureAwait(false);
                return new TargetResolution(true, SidebarTargetResolver.Resolve(
                    item.ThreadId, grouping, sessionIndex, globalState));
            },
            token).ConfigureAwait(false);

        if (!resolution.GroupingFound)
            return "已打开对话；无法确定侧栏分组";
        if (resolution.Target is null)
            return "已打开对话；无法读取 Codex 会话索引";

        var result = await scroller.RevealAsync(handle, resolution.Target, token).ConfigureAwait(false);
        return result.Status switch
        {
            SidebarScrollStatus.Found => null,
            SidebarScrollStatus.Ambiguous => "已打开对话；侧栏有同名任务，已停止定位",
            SidebarScrollStatus.RegionUnavailable => "已打开对话；Codex 侧栏结构已变化",
            _ => "已打开对话；暂时无法在侧栏定位"
        };
    }

    private async Task<bool> IsUiAutomationRootReadyAsync(nint handle, long remainingTicks, CancellationToken token)
    {
        var remaining = TimeSpan.FromSeconds((double)remainingTicks / time.TimestampFrequency);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var pending = Task.Run(async () =>
            {
                linked.Token.ThrowIfCancellationRequested();
                await rootReadiness.ProbeAsync(handle, linked.Token).ConfigureAwait(false);
            }, CancellationToken.None);
            await pending.WaitAsync(remaining, time, token).ConfigureAwait(false);
            return true;
        }
        catch (UiAutomationRootUnavailableException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            linked.Cancel();
            return false;
        }
    }

    private sealed record TargetResolution(bool GroupingFound, SidebarTarget? Target);
}
