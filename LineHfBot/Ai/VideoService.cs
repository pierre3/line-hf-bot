using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LineHfBot.Configuration;
using LineHfBot.Media;
using Microsoft.Extensions.Options;

namespace LineHfBot.Ai;

/// <summary>Generates a video from a text prompt via Hugging Face Inference.</summary>
public interface IVideoService
{
    Task<GeneratedMedia> GenerateAsync(string prompt, CancellationToken cancellationToken);
}

/// <summary>
/// Calls the HF text-to-video endpoint. Handles both response styles:
/// raw video bytes, or a JSON body that contains a URL to the generated video (which is then fetched).
/// The endpoint is configurable because video support is provider-dependent.
/// </summary>
public sealed class HuggingFaceVideoService(HttpClient http, IOptions<HuggingFaceOptions> options) : IVideoService
{
    public async Task<GeneratedMedia> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        var url = opt.VideoEndpoint.Replace("{model}", opt.VideoModel);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.ApiKey);
        request.Content = JsonContent.Create(new { inputs = prompt });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opt.VideoTimeoutSeconds)));

        using var response = await http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

        // Some providers return JSON with a URL to the video rather than the bytes directly.
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var videoUrl = ExtractVideoUrl(json)
                ?? throw new InvalidOperationException("Video URL not found in provider response.");
            var bytesFromUrl = await http.GetByteArrayAsync(videoUrl, cts.Token);
            return new GeneratedMedia(bytesFromUrl, "video/mp4");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        return new GeneratedMedia(bytes, string.IsNullOrEmpty(contentType) ? "video/mp4" : contentType);
    }

    /// <summary>Best-effort extraction of a video URL from common provider JSON shapes.</summary>
    private static string? ExtractVideoUrl(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
        {
            return u.GetString();
        }
        if (root.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.String)
        {
            return o.GetString();
        }
        if (root.TryGetProperty("video", out var v))
        {
            if (v.ValueKind == JsonValueKind.String)
            {
                return v.GetString();
            }
            if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("url", out var vu) && vu.ValueKind == JsonValueKind.String)
            {
                return vu.GetString();
            }
        }
        return null;
    }
}
