using System.Diagnostics;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Windows.Services;

public interface ICodexDeepLinkLauncher
{
    bool Open(string threadId);
}

public sealed class CodexDeepLinkLauncher : ICodexDeepLinkLauncher
{
    public bool Open(string threadId)
    {
        if (!CodexThreadLink.TryCreate(threadId, out var uri))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
