using System.Net;
using System.Text;
using System.Text.Json;
using CodexTaskMonitor.Windows.Notifications;

namespace CodexTaskMonitor.Tests.Notifications;

public sealed class BarkNotificationClientTests
{
    [Fact]
    public async Task SendAsync_PostsOnlyApprovedNotificationFields()
    {
        HttpMethod? capturedMethod = null;
        Uri? capturedUri = null;
        string? capturedBody = null;
        var handler = new RecordingHandler(async request =>
        {
            capturedMethod = request.Method;
            capturedUri = request.RequestUri;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return Response(
                HttpStatusCode.OK,
                "{\"code\":200,\"message\":\"success\",\"timestamp\":1786996800}");
        });
        using var http = new HttpClient(handler);
        var client = new BarkNotificationClient(http);

        var result = await client.SendAsync(
            new Uri("https://example.invalid/device-key"),
            new BarkNotification("Task title", "Project name", "Codex Task Monitor"),
            default);

        Assert.True(result.Succeeded);
        Assert.Equal(BarkSendError.None, result.Error);
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal(new Uri("https://example.invalid/device-key"), capturedUri);
        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal(["body", "group", "title"], body.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("Task title", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("Project name", body.RootElement.GetProperty("body").GetString());
        Assert.Equal("Codex Task Monitor", body.RootElement.GetProperty("group").GetString());
    }

    [Fact]
    public async Task SendAsync_MapsHttpAndBarkFailuresWithoutResponseContent()
    {
        var responseText = "private-response-content";
        using var httpFailure = new HttpClient(new RecordingHandler(_ =>
            Task.FromResult(Response(HttpStatusCode.BadGateway, responseText))));
        using var barkFailure = new HttpClient(new RecordingHandler(_ =>
            Task.FromResult(Response(HttpStatusCode.OK,
                "{\"code\":400,\"message\":\"private-response-content\",\"timestamp\":1786996800}"))));
        var notification = new BarkNotification("title", "body", "group");

        var httpResult = await new BarkNotificationClient(httpFailure).SendAsync(
            new Uri("https://example.invalid/device-key"), notification, default);
        var barkResult = await new BarkNotificationClient(barkFailure).SendAsync(
            new Uri("https://example.invalid/device-key"), notification, default);

        Assert.Equal(BarkSendError.Http, httpResult.Error);
        Assert.Equal(BarkSendError.Rejected, barkResult.Error);
        Assert.DoesNotContain(responseText, httpResult.ToString());
        Assert.DoesNotContain(responseText, barkResult.ToString());
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
