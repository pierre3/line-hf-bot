using System.Text.Json;
using LineHfBot.Configuration;
using LineHfBot.Media;
using Microsoft.Extensions.Options;

namespace LineHfBot.Ai;

/// <summary>Generates a video from an existing image plus a motion prompt via Hugging Face image-to-video.</summary>
public interface IImageToVideoService
{
    Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string referenceContentType, string prompt, CancellationToken cancellationToken);
}

/// <summary>
/// Image-to-video via the fal-ai provider on the Hugging Face router (default model
/// fal-ai/wan/v2.2-a14b/image-to-video). hf-inference does not serve image-to-video, so a GPU provider is
/// required. This is the combination of image-to-image (the reference image rides as a base64 data URI, as
/// in <see cref="HuggingFaceImageEditService"/>) and text-to-video (the fal async queue and video.url result,
/// as in <see cref="HuggingFaceVideoService"/>). The queue mechanics (submit → poll → result → SSRF-guarded
/// re-fetch) live in the shared <see cref="FalQueue"/>; here we only build the request body
/// ({image_url, prompt}) and extract video.url from the result. It shares the text-to-video timeout.
/// </summary>
public sealed class HuggingFaceImageToVideoService(HttpClient http, IOptions<HuggingFaceOptions> options) : IImageToVideoService
{
    public async Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string referenceContentType, string prompt, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opt.VideoTimeoutSeconds)));
        var ct = cts.Token;

        // 1. Submit the job. The reference image rides as a base64 data URI (no hosting needed); the motion
        // prompt is an optional fal parameter that we always supply from the user's instruction.
        // aspect_ratio must be one of the discrete ratios the distributed GPU endpoint supports — the default
        // 'auto' derives the output size from the input image and fails (422) for many photo sizes. We pick the
        // supported ratio closest to the input's own aspect ratio to minimize cropping/distortion.
        var submitUrl = opt.ImageToVideoEndpoint.Replace("{model}", opt.ImageToVideoModel);
        var mime = string.IsNullOrEmpty(referenceContentType) ? "image/png" : referenceContentType;
        var dataUri = $"data:{mime};base64,{Convert.ToBase64String(referenceImage)}";
        var (statusUrl, responseUrl) = await FalQueue.SubmitAsync(http, submitUrl, new
        {
            image_url = dataUri,
            prompt,
            aspect_ratio = ResolveAspectRatio(referenceImage),
        }, opt.ApiKey, ct);

        // 2. Poll until the job reaches a terminal state, then read the result video URL.
        await FalQueue.PollUntilCompletedAsync(http, statusUrl, opt.ApiKey, ct);
        using var doc = await FalQueue.GetResultAsync(http, responseUrl, opt.ApiKey, ct);
        var videoUrl = ExtractVideoUrl(doc.RootElement);

        // 3. Re-fetch the final video through the SSRF-guarded helper (fal.media must be allowlisted).
        var allowed = MediaRefetch.ParseHosts(opt.MediaRefetchAllowedHosts);
        var (bytes, refetchedType) = await MediaRefetch.FetchAsync(http, videoUrl, allowed, ct);
        return new GeneratedMedia(bytes, string.IsNullOrEmpty(refetchedType) ? "video/mp4" : refetchedType);
    }

    // Supported output aspect ratios for the fal wan i2v distributed GPU endpoint. Pick the one closest to
    // the input image's aspect ratio (by log-ratio distance); fall back to square when dimensions are unknown.
    private static readonly (string Label, double Ratio)[] SupportedAspectRatios =
        [("16:9", 16.0 / 9.0), ("1:1", 1.0), ("9:16", 9.0 / 16.0)];

    private static string ResolveAspectRatio(byte[] image)
    {
        if (ImageDimensions.TryGet(image) is not { Width: > 0, Height: > 0 } dim)
        {
            return "1:1";
        }
        var ratio = (double)dim.Width / dim.Height;
        return SupportedAspectRatios.MinBy(c => Math.Abs(Math.Log(ratio / c.Ratio))).Label;
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
