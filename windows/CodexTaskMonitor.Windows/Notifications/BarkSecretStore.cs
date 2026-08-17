using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexTaskMonitor.Windows.Notifications;

public interface IBarkSecretStore
{
    Task<BarkSecret?> LoadAsync(CancellationToken token);
}

public sealed class BarkSecretStore(string path) : IBarkSecretStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public async Task<BarkSecret?> LoadAsync(CancellationToken token)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var protectedBytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            try
            {
                var document = JsonSerializer.Deserialize<BarkSecretDocument>(clearBytes, Options);
                if (document is null || document.ConfigurationId == Guid.Empty ||
                    !Uri.TryCreate(document.Endpoint, UriKind.Absolute, out var endpoint) ||
                    endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) ||
                    !string.IsNullOrEmpty(endpoint.Fragment))
                {
                    return null;
                }

                return new BarkSecret(document.ConfigurationId, endpoint);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    private sealed record BarkSecretDocument(Guid ConfigurationId, string Endpoint);
}
