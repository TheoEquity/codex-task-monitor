using System.IO;
using System.Text.Json;

namespace CodexTaskMonitor.Windows.Services;

public interface ILocalDiagnostics
{
    Task WriteAsync(string category, TimeSpan duration, int count, CancellationToken token);
}

public sealed class LocalDiagnostics : ILocalDiagnostics
{
    private static readonly HashSet<string> SafeCategories = new(StringComparer.Ordinal)
    {
        "monitor-scan-complete",
        "monitor-scan-failure",
        "startup-registration-failure",
        "uia-failure",
        "uia-timeout",
        "deep-link-failed",
        "reveal-ok",
        "reveal-warning",
        "reveal-error"
    };

    private readonly string directory;
    private readonly long maxBytes;
    private readonly int retainedFiles;
    private readonly SemaphoreSlim gate = new(1, 1);

    public LocalDiagnostics(string directory, long maxBytes = 1_048_576, int retainedFiles = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedFiles, 1);

        this.directory = directory;
        this.maxBytes = maxBytes;
        this.retainedFiles = retainedFiles;
    }

    public async Task WriteAsync(string category, TimeSpan duration, int count, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "monitor.log");
            if (File.Exists(path) && new FileInfo(path).Length >= maxBytes)
                Rotate();

            var line = JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow,
                category = SafeCategories.Contains(category) ? category : "other",
                duration_ms = (long)duration.TotalMilliseconds,
                count
            });
            await File.AppendAllTextAsync(path, line + Environment.NewLine, token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private void Rotate()
    {
        if (retainedFiles == 1)
        {
            File.Delete(Path.Combine(directory, "monitor.log"));
            return;
        }

        for (var index = retainedFiles - 1; index >= 1; index--)
        {
            var source = Path.Combine(directory, index == 1 ? "monitor.log" : $"monitor.{index - 1}.log");
            var target = Path.Combine(directory, $"monitor.{index}.log");
            if (File.Exists(source))
                File.Move(source, target, overwrite: true);
        }
    }
}
