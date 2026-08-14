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
/// Calls the HF text-to-image endpoint and returns the raw image bytes.
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

        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
        return new GeneratedMedia(bytes, contentType);
    }
}
