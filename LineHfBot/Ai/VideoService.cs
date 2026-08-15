using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// raw video bytes, or a JSON body that contains a URL to the generated video (re-fetched
/// through the shared SSRF-guarded helper).
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
        await HfHttp.EnsureSuccessAsync(response, cts.Token);

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

        // Some providers return JSON with a URL to the video rather than the bytes directly.
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var videoUrl = MediaUrlExtractor.TryExtract(json)
                ?? throw new InvalidOperationException("Video URL not found in provider response.");
            var allowed = MediaRefetch.ParseHosts(opt.MediaRefetchAllowedHosts);
            var (bytesFromUrl, refetchedType) = await MediaRefetch.FetchAsync(http, videoUrl, allowed, cts.Token);
            return new GeneratedMedia(bytesFromUrl, string.IsNullOrEmpty(refetchedType) ? "video/mp4" : refetchedType);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        return new GeneratedMedia(bytes, string.IsNullOrEmpty(contentType) ? "video/mp4" : contentType);
    }
}
