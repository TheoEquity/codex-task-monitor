using CodexTaskMonitor.Windows.ViewModels;
using Microsoft.Win32;

namespace CodexTaskMonitor.Windows.Services;

public interface IRunValueStore
{
    string? Read();

    void Write(string value);

    void Delete();
}

public sealed class RegistryRunValueStore : IRunValueStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexTaskMonitor";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string;
    }

    public void Write(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, value);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

public sealed class StartupRegistration : IStartupRegistration
{
    private readonly IRunValueStore values;
    private readonly string executablePath;

    public StartupRegistration(IRunValueStore values, string executablePath)
    {
        this.values = values ?? throw new ArgumentNullException(nameof(values));
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        this.executablePath = executablePath;
    }

    public bool IsEnabled => string.Equals(values.Read(), Command, StringComparison.OrdinalIgnoreCase);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
            values.Write(Command);
        else
            values.Delete();
    }

    private string Command => $"\"{executablePath}\"";
}
