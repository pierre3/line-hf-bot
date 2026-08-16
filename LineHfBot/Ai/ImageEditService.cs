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
/// fal-ai/qwen-image-edit). hf-inference does not serve image-to-image, so this model/provider is required.
/// The fal async queue (submit → poll → result → SSRF-guarded re-fetch) lives in the shared
/// <see cref="FalQueue"/>; here we only build the image-to-image request body and extract images[0].url.
/// </summary>
public sealed class HuggingFaceImageEditService(HttpClient http, IOptions<HuggingFaceOptions> options) : IImageEditService
{
    public async Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string instruction, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opt.ImageEditTimeoutSeconds)));
        var ct = cts.Token;

        // 1. Submit the job. The reference image rides as a base64 data URI (no hosting needed).
        var submitUrl = opt.ImageEditEndpoint.Replace("{model}", opt.ImageEditModel);
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(referenceImage)}";
        var (statusUrl, responseUrl) = await FalQueue.SubmitAsync(http, submitUrl, new
        {
            prompt = instruction,
            image_url = dataUri,
            image_urls = new[] { dataUri },
        }, opt.ApiKey, ct);

        // 2. Poll until the job reaches a terminal state, then read the result image URL.
        await FalQueue.PollUntilCompletedAsync(http, statusUrl, opt.ApiKey, ct);
        using var doc = await FalQueue.GetResultAsync(http, responseUrl, opt.ApiKey, ct);
        var imageUrl = ExtractImageUrl(doc.RootElement);

        // 3. Re-fetch the final image through the SSRF-guarded helper (fal.media must be allowlisted).
        var allowed = MediaRefetch.ParseHosts(opt.MediaRefetchAllowedHosts);
        var (bytes, refetchedType) = await MediaRefetch.FetchAsync(http, imageUrl, allowed, ct);
        return new GeneratedMedia(bytes, string.IsNullOrEmpty(refetchedType) ? "image/png" : refetchedType);
    }

    private static string ExtractImageUrl(JsonElement root)
    {
        if (root.TryGetProperty("images", out var images) &&
            images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0 &&
            images[0].TryGetProperty("url", out var url) && url.GetString() is { Length: > 0 } imageUrl)
        {
            return imageUrl;
        }
        throw new InvalidOperationException("fal result did not contain images[0].url.");
    }
}
