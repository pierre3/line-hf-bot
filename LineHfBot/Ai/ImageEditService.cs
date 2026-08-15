using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// Calls the HF image-to-image endpoint (default model Qwen/Qwen-Image-Edit). The reference image is
/// sent as base64 in "inputs" with the edit instruction in "parameters.prompt" (this payload differs
/// from text-to-image and cannot reuse it). The response is raw image bytes or JSON-with-URL, handled
/// by the shared <see cref="MediaResponse"/> path (same SSRF-guarded re-fetch as the image path).
/// </summary>
public sealed class HuggingFaceImageEditService(HttpClient http, IOptions<HuggingFaceOptions> options) : IImageEditService
{
    public async Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string instruction, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        var url = opt.ImageEditEndpoint.Replace("{model}", opt.ImageEditModel);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.ApiKey);
        request.Content = JsonContent.Create(new
        {
            inputs = Convert.ToBase64String(referenceImage),
            parameters = new { prompt = instruction },
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opt.ImageEditTimeoutSeconds)));

        using var response = await http.SendAsync(request, cts.Token);
        await HfHttp.EnsureSuccessAsync(response, cts.Token);

        var allowed = MediaRefetch.ParseHosts(opt.MediaRefetchAllowedHosts);
        return await MediaResponse.ReadAsync(http, response, allowed, "image/png", cts.Token);
    }
}
