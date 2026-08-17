using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexTaskMonitor.Windows.Notifications;

namespace CodexTaskMonitor.Tests.Notifications;

public sealed class BarkSecretStoreTests
{
    [Fact]
    public async Task MissingFile_ReturnsNull()
    {
        var store = new BarkSecretStore(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dat"));

        Assert.Null(await store.LoadAsync(default));
    }

    [Fact]
    public async Task CurrentUserDpapiEnvelope_LoadsEndpoint()
    {
        var path = TemporaryPath();
        try
        {
            var configurationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var document = JsonSerializer.SerializeToUtf8Bytes(new
            {
                configurationId,
                endpoint = "https://example.invalid/device-key"
            });
            var encrypted = ProtectedData.Protect(document, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, encrypted);

            var secret = await new BarkSecretStore(path).LoadAsync(default);

            Assert.NotNull(secret);
            Assert.Equal(configurationId, secret.ConfigurationId);
            Assert.Equal(new Uri("https://example.invalid/device-key"), secret.Endpoint);
        }
        finally
        {
            DeleteParent(path);
        }
    }

    [Fact]
    public async Task InvalidCiphertext_ReturnsNullWithoutIncludingFileBytes()
    {
        var path = TemporaryPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "not-a-secret", Encoding.UTF8);

            Assert.Null(await new BarkSecretStore(path).LoadAsync(default));
        }
        finally
        {
            DeleteParent(path);
        }
    }

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"bark-secret-{Guid.NewGuid():N}", "bark-secret.dat");

    private static void DeleteParent(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
