using System.ComponentModel;
using System.Diagnostics;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Windows.Services;

public sealed class CodexLaunchTimeProvider : ICodexLaunchTimeProvider
{
    public DateTimeOffset? GetLaunchTime()
    {
        DateTimeOffset? newest = null;
        try
        {
            foreach (var process in Process.GetProcessesByName("ChatGPT"))
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                            continue;

                        var started = new DateTimeOffset(process.StartTime.ToUniversalTime());
                        if (newest is null || started > newest)
                            newest = started;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (Win32Exception)
                    {
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }

        return newest;
    }
}
