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
/// Text-to-video via the fal-ai provider on the Hugging Face router (default model
/// fal-ai/wan/v2.2-5b/text-to-video). hf-inference does not serve text-to-video, so a GPU provider is
/// required; fal uses the same async queue as image-to-image. The queue mechanics (submit → poll →
/// result → SSRF-guarded re-fetch) live in the shared <see cref="FalQueue"/>; here we only build the
/// text-to-video request body ({prompt}) and extract video.url from the result.
/// The endpoint/model are configurable so an operator can target a different provider.
/// </summary>
public sealed class HuggingFaceVideoService(HttpClient http, IOptions<HuggingFaceOptions> options) : IVideoService
{
    public async Task<GeneratedMedia> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opt.VideoTimeoutSeconds)));
        var ct = cts.Token;

        // 1. Submit the job (fal text-to-video takes just the prompt).
        var submitUrl = opt.VideoEndpoint.Replace("{model}", opt.VideoModel);
        var (statusUrl, responseUrl) = await FalQueue.SubmitAsync(http, submitUrl, new { prompt }, opt.ApiKey, ct);

        // 2. Poll until the job reaches a terminal state, then read the result video URL.
        await FalQueue.PollUntilCompletedAsync(http, statusUrl, opt.ApiKey, ct);
        using var doc = await FalQueue.GetResultAsync(http, responseUrl, opt.ApiKey, ct);
        var videoUrl = ExtractVideoUrl(doc.RootElement);

        // 3. Re-fetch the final video through the SSRF-guarded helper (fal.media must be allowlisted).
        var allowed = MediaRefetch.ParseHosts(opt.MediaRefetchAllowedHosts);
        var (bytes, refetchedType) = await MediaRefetch.FetchAsync(http, videoUrl, allowed, ct);
        return new GeneratedMedia(bytes, string.IsNullOrEmpty(refetchedType) ? "video/mp4" : refetchedType);
    }

    private static string ExtractVideoUrl(JsonElement root)
    {
        if (root.TryGetProperty("video", out var video) && video.ValueKind == JsonValueKind.Object &&
            video.TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } videoUrl)
        {
            return videoUrl;
        }
        throw new InvalidOperationException("fal result did not contain video.url.");
    }
}
