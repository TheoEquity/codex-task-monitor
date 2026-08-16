using System.ComponentModel;
using System.Diagnostics;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class ChatGptWindowLocator : IChatGptWindowLocator
{
    public nint FindMainWindow()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT").OrderByDescending(SafeStartTime))
        {
            try
            {
                if (!process.HasExited && process.MainWindowHandle != 0)
                    return process.MainWindowHandle;
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return 0;
    }

    private static DateTime SafeStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (InvalidOperationException)
        {
            return DateTime.MinValue;
        }
        catch (Win32Exception)
        {
            return DateTime.MinValue;
        }
    }
}
