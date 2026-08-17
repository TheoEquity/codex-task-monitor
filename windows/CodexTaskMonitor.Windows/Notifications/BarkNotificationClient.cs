using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace CodexTaskMonitor.Windows.Notifications;

public enum BarkSendError
{
    None,
    Network,
    Timeout,
    Http,
    Rejected,
    InvalidResponse
}

public readonly record struct BarkSendResult(bool Succeeded, BarkSendError Error)
{
    public static BarkSendResult Success { get; } = new(true, BarkSendError.None);

    public static BarkSendResult Failure(BarkSendError error) => new(false, error);
}

public interface IBarkNotificationClient
{
    Task<BarkSendResult> SendAsync(Uri endpoint, BarkNotification notification, CancellationToken token);
}

public sealed class BarkNotificationClient(HttpClient http) : IBarkNotificationClient
{
    private static readonly JsonSerializerOptions ResponseOptions = new(JsonSerializerDefaults.Web);

    public async Task<BarkSendResult> SendAsync(
        Uri endpoint,
        BarkNotification notification,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(notification);

        try
        {
            using var response = await http.PostAsJsonAsync(endpoint, new
            {
                title = notification.Title,
                body = notification.Body,
                group = notification.Group
            }, token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return BarkSendResult.Failure(BarkSendError.Http);

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var document = await JsonSerializer.DeserializeAsync<BarkResponse>(
                    stream, ResponseOptions, cancellationToken: token)
                .ConfigureAwait(false);
            return document?.Code == 200
                ? BarkSendResult.Success
                : BarkSendResult.Failure(document is null ? BarkSendError.InvalidResponse : BarkSendError.Rejected);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return BarkSendResult.Failure(BarkSendError.Timeout);
        }
        catch (HttpRequestException)
        {
            return BarkSendResult.Failure(BarkSendError.Network);
        }
        catch (JsonException)
        {
            return BarkSendResult.Failure(BarkSendError.InvalidResponse);
        }
    }

    private sealed record BarkResponse(int Code, string? Message, long? Timestamp);
}
