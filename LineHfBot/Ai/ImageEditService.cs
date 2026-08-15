using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LineHfBot.Configuration;
using LineHfBot.Media;
using Microsoft.Extensions.Options;

namespace LineHfBot.Ai;

/// <summary>Edits an existing image from a text instruction via Hugging Face image-to-image.</summary>
public interface IImageEditService
{
    Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string instruction, CancellationToken cancellationToken);
}

/// <summary>
/// Image-to-image via the fal-ai provider on the Hugging Face router (default model
/// fal-ai/qwen-image-edit). Unlike hf-inference's single POST, fal is an async queue: submit the job,
/// poll its status until COMPLETED, then read the result which carries the output image URL.
/// hf-inference does not serve image-to-image, so this model/provider is required.
///
/// The queue's status/response URLs are returned pointing at queue.fal.run (which rejects the HF token
/// with 401). We rewrite them to the router host and only ever send the HF token there — never to an
/// arbitrary host — then re-fetch the final fal.media image through the shared SSRF-guarded helper.
/// </summary>
public sealed class HuggingFaceImageEditService(HttpClient http, IOptions<HuggingFaceOptions> options) : IImageEditService
{
    private const string QueuePrefix = "https://queue.fal.run/";
    private const string RouterPrefix = "https://router.huggingface.co/fal-ai/";

    public async Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string instruction, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opt.ImageEditTimeoutSeconds)));
        var ct = cts.Token;

        // 1. Submit the job. The reference image rides as a base64 data URI (no hosting needed).
        var submitUrl = opt.ImageEditEndpoint.Replace("{model}", opt.ImageEditModel);
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(referenceImage)}";
        var (statusUrl, responseUrl) = await SubmitAsync(submitUrl, instruction, dataUri, opt.ApiKey, ct);

        // 2. Poll until the job reaches a terminal state.
        await PollUntilCompletedAsync(statusUrl, opt.ApiKey, ct);

        // 3. Read the result and extract the output image URL.
        var imageUrl = await ReadResultUrlAsync(responseUrl, opt.ApiKey, ct);

        // 4. Re-fetch the final image through the SSRF-guarded helper (fal.media must be allowlisted).
        var allowed = MediaRefetch.ParseHosts(opt.MediaRefetchAllowedHosts);
        var (bytes, refetchedType) = await MediaRefetch.FetchAsync(http, imageUrl, allowed, ct);
        return new GeneratedMedia(bytes, string.IsNullOrEmpty(refetchedType) ? "image/png" : refetchedType);
    }

    private async Task<(string StatusUrl, string ResponseUrl)> SubmitAsync(
        string submitUrl, string instruction, string dataUri, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, submitUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            prompt = instruction,
            image_url = dataUri,
            image_urls = new[] { dataUri },
        });

        using var response = await http.SendAsync(request, ct);
        await HfHttp.EnsureSuccessAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
        var status = ToRouterUrl(GetString(doc.RootElement, "status_url"));
        var result = ToRouterUrl(GetString(doc.RootElement, "response_url"));
        return (status, result);
    }

    private async Task PollUntilCompletedAsync(string statusUrl, string apiKey, CancellationToken ct)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            using var doc = await GetJsonAsync(statusUrl, apiKey, ct);
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

    private async Task<string> ReadResultUrlAsync(string responseUrl, string apiKey, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(responseUrl, apiKey, ct);
        if (doc.RootElement.TryGetProperty("images", out var images) &&
            images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0 &&
            images[0].TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } imageUrl)
        {
            return imageUrl;
        }
        throw new InvalidOperationException("fal result did not contain images[0].url.");
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string apiKey, CancellationToken ct)
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
