using System.Net.Http.Headers;
using System.Net.Http.Json;
using LineHfBot.Configuration;
using LineHfBot.Media;
using Microsoft.Extensions.Options;

namespace LineHfBot.Ai;

/// <summary>Generates an image from a text prompt via Hugging Face Inference.</summary>
public interface IImageService
{
    Task<GeneratedMedia> GenerateAsync(string prompt, CancellationToken cancellationToken);
}

/// <summary>
/// Calls the HF text-to-image endpoint. Handles both response styles like the video path:
/// raw image bytes, or a JSON body containing a URL to the generated image (which is then
/// re-fetched through the shared SSRF-guarded helper).
/// The endpoint is configurable because image support is provider-dependent (see HuggingFaceOptions.ImageEndpoint).
/// </summary>
public sealed class HuggingFaceImageService(HttpClient http, IOptions<HuggingFaceOptions> options) : IImageService
{
    public async Task<GeneratedMedia> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        var url = opt.ImageEndpoint.Replace("{model}", opt.ImageModel);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.ApiKey);
        request.Content = JsonContent.Create(new { inputs = prompt });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opt.ImageTimeoutSeconds)));

        using var response = await http.SendAsync(request, cts.Token);
        await HfHttp.EnsureSuccessAsync(response, cts.Token);

        // Some providers (e.g. FLUX via fal-ai) return JSON with a URL rather than the image bytes.
        var allowed = MediaRefetch.ParseHosts(opt.MediaRefetchAllowedHosts);
        return await MediaResponse.ReadAsync(http, response, allowed, "image/png", cts.Token);
    }
}
