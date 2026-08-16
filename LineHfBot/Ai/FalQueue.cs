using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LineHfBot.Ai;

/// <summary>
/// Shared client for fal-ai's asynchronous queue on the Hugging Face router, used by both
/// image-to-image (edit) and text-to-video. Unlike hf-inference's single POST, fal is a queue:
/// submit the job, poll its status until COMPLETED, then read the result which carries the output URL.
///
/// The queue's status/response URLs are returned pointing at queue.fal.run (which rejects the HF token
/// with 401). We rewrite them to the router host and only ever send the HF token there — never to an
/// arbitrary host returned by the provider. Extracting the output media URL from the result document is
/// task-specific (images[0].url vs video.url), so callers do that themselves.
/// </summary>
internal static class FalQueue
{
    private const string QueuePrefix = "https://queue.fal.run/";
    private const string RouterPrefix = "https://router.huggingface.co/fal-ai/";

    /// <summary>Submit a job and return the status/response URLs already rewritten to the router host.</summary>
    public static async Task<(string StatusUrl, string ResponseUrl)> SubmitAsync(
        HttpClient http, string submitUrl, object body, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, submitUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request, ct);
        await HfHttp.EnsureSuccessAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
        var status = ToRouterUrl(GetString(doc.RootElement, "status_url"));
        var result = ToRouterUrl(GetString(doc.RootElement, "response_url"));
        return (status, result);
    }

    /// <summary>Poll the status URL (~1s interval) until the job reaches COMPLETED; throw on any other terminal state.</summary>
    public static async Task PollUntilCompletedAsync(HttpClient http, string statusUrl, string apiKey, CancellationToken ct)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            using var doc = await GetJsonAsync(http, statusUrl, apiKey, ct);
            var status = GetString(doc.RootElement, "status");
            if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!string.Equals(status, "IN_QUEUE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"fal job did not complete (status: {status}).");
            }
        }
    }

    /// <summary>GET the completed job's result document. The caller extracts the task-specific media URL.</summary>
    public static Task<JsonDocument> GetResultAsync(HttpClient http, string responseUrl, string apiKey, CancellationToken ct) =>
        GetJsonAsync(http, responseUrl, apiKey, ct);

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string url, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, ct);
        await HfHttp.EnsureSuccessAsync(response, ct);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";

    /// <summary>
    /// Rewrite a fal queue URL (status/response) to the router host and add the queue subdomain flag, so
    /// polling authenticates with the HF token. Only queue.fal.run URLs are accepted — the HF token must
    /// never be sent to an arbitrary host returned by the provider.
    /// </summary>
    internal static string ToRouterUrl(string url)
    {
        if (!url.StartsWith(QueuePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected fal queue URL (refusing to send credentials).");
        }
        var rewritten = RouterPrefix + url[QueuePrefix.Length..];
        return rewritten + (rewritten.Contains('?') ? "&" : "?") + "_subdomain=queue";
    }
}
