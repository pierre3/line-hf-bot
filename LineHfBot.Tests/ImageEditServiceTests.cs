using System.Text;
using System.Text.Json;
using LineHfBot.Ai;
using LineHfBot.Configuration;
using Microsoft.Extensions.Options;

namespace LineHfBot.Tests;

public class ImageEditServiceTests
{
    private static readonly byte[] ReferenceImage = Encoding.UTF8.GetBytes("reference-image-bytes");
    private static readonly byte[] EditedImage = Encoding.UTF8.GetBytes("edited-image-bytes");

    private static IOptions<HuggingFaceOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new HuggingFaceOptions
        {
            ApiKey = "hf_test_token",
            ImageEditModel = "Qwen/Qwen-Image-Edit",
            ImageEditEndpoint = "https://router.huggingface.co/hf-inference/models/{model}",
            MediaRefetchAllowedHosts = "fal.media;replicate.delivery",
            ImageEditTimeoutSeconds = 30,
        });

    // The img2img payload differs from text-to-image: base64 image in "inputs", instruction in "parameters.prompt".
    [Fact]
    public async Task Sends_base64_image_and_prompt_payload()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(EditedImage, "image/png"));
        var svc = new HuggingFaceImageEditService(new HttpClient(handler), Options());

        await svc.GenerateAsync(ReferenceImage, "add a red hat", CancellationToken.None);

        var post = handler.Seen[0];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.True(post.HasAuthorization);
        Assert.Equal("https://router.huggingface.co/hf-inference/models/Qwen/Qwen-Image-Edit", post.Uri!.ToString());

        using var doc = JsonDocument.Parse(post.Body!);
        var root = doc.RootElement;
        Assert.Equal(Convert.ToBase64String(ReferenceImage), root.GetProperty("inputs").GetString());
        Assert.Equal("add a red hat", root.GetProperty("parameters").GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task Raw_bytes_response_returns_edited_image()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(EditedImage, "image/png"));
        var svc = new HuggingFaceImageEditService(new HttpClient(handler), Options());

        var media = await svc.GenerateAsync(ReferenceImage, "make it night", CancellationToken.None);

        Assert.Equal(EditedImage, media.Bytes);
        Assert.Equal("image/png", media.ContentType);
        Assert.Single(handler.Seen);
    }

    // Provider may return JSON-URL (e.g. fal-ai); reuse the shared SSRF-guarded re-fetch.
    [Fact]
    public async Task Json_url_response_is_refetched_without_authorization()
    {
        var handler = new StubHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? StubHttpMessageHandler.Json("{\"images\":[{\"url\":\"https://cdn.fal.media/edited.png\"}]}")
            : StubHttpMessageHandler.Bytes(EditedImage, "image/png"));
        var svc = new HuggingFaceImageEditService(new HttpClient(handler), Options());

        var media = await svc.GenerateAsync(ReferenceImage, "add snow", CancellationToken.None);

        Assert.Equal(EditedImage, media.Bytes);
        Assert.Equal(HttpMethod.Get, handler.Seen[1].Method);
        Assert.Equal("cdn.fal.media", handler.Seen[1].Uri!.Host);
        Assert.False(handler.Seen[1].HasAuthorization);
    }

    [Fact]
    public async Task Json_url_on_disallowed_host_is_rejected()
    {
        var handler = new StubHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? StubHttpMessageHandler.Json("{\"url\":\"https://evil.example.com/edited.png\"}")
            : StubHttpMessageHandler.Bytes(EditedImage, "image/png"));
        var svc = new HuggingFaceImageEditService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync(ReferenceImage, "add snow", CancellationToken.None));
        Assert.Single(handler.Seen); // POST only; no re-fetch
    }

    [Fact]
    public void ImageEdit_defaults_match_docs()
    {
        var o = new HuggingFaceOptions();
        Assert.Equal("Qwen/Qwen-Image-Edit", o.ImageEditModel);
        Assert.Equal("https://router.huggingface.co/hf-inference/models/{model}", o.ImageEditEndpoint);
        Assert.Equal(120, o.ImageEditTimeoutSeconds);
    }
}
