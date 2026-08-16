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
            ImageEditModel = "fal-ai/qwen-image-edit",
            ImageEditEndpoint = "https://router.huggingface.co/fal-ai/{model}?_subdomain=queue",
            MediaRefetchAllowedHosts = "fal.media;replicate.delivery",
            ImageEditTimeoutSeconds = 30,
        });

    // fal returns queue.fal.run status/response URLs; a submit response fixture with a given result URL.
    private static StubHttpMessageHandler FalHandler(string resultImageUrl) => new(req =>
    {
        if (req.Method == HttpMethod.Post)
        {
            return StubHttpMessageHandler.Json(
                "{\"status\":\"IN_QUEUE\"," +
                "\"status_url\":\"https://queue.fal.run/fal-ai/qwen-image-edit/requests/req-1/status\"," +
                "\"response_url\":\"https://queue.fal.run/fal-ai/qwen-image-edit/requests/req-1\"}");
        }
        var path = req.RequestUri!.AbsolutePath;
        if (path.EndsWith("/status", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Json("{\"status\":\"COMPLETED\"}");
        }
        if (req.RequestUri!.Host.EndsWith("fal.media", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Bytes(EditedImage, "image/png");
        }
        // The result endpoint.
        return StubHttpMessageHandler.Json($"{{\"images\":[{{\"url\":\"{resultImageUrl}\"}}]}}");
    });

    [Fact]
    public async Task Submit_posts_prompt_and_base64_data_uri_with_auth()
    {
        var handler = FalHandler("https://cdn.fal.media/edited.png");
        var svc = new HuggingFaceImageEditService(new HttpClient(handler), Options());

        await svc.GenerateAsync(ReferenceImage, "add a red hat", CancellationToken.None);

        var post = handler.Seen[0];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.True(post.HasAuthorization);
        Assert.Equal("https://router.huggingface.co/fal-ai/fal-ai/qwen-image-edit?_subdomain=queue", post.Uri!.ToString());

        using var doc = JsonDocument.Parse(post.Body!);
        var root = doc.RootElement;
        Assert.Equal("add a red hat", root.GetProperty("prompt").GetString());
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(ReferenceImage)}", root.GetProperty("image_url").GetString());
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(ReferenceImage)}", root.GetProperty("image_urls")[0].GetString());
    }

    // Poll + result URLs are rewritten to the router host (HF token only goes to router.huggingface.co),
    // and the final fal.media image is re-fetched WITHOUT Authorization.
    [Fact]
    public async Task Polls_via_router_and_refetches_result_without_authorization()
    {
        var handler = FalHandler("https://v3b.fal.media/files/edited.png");
        var svc = new HuggingFaceImageEditService(new HttpClient(handler), Options());

        var media = await svc.GenerateAsync(ReferenceImage, "make it night", CancellationToken.None);

        Assert.Equal(EditedImage, media.Bytes);

        // status poll went to the router host with auth.
        var status = handler.Seen.First(s => s.Uri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal));
        Assert.Equal("router.huggingface.co", status.Uri!.Host);
        Assert.Contains("_subdomain=queue", status.Uri!.Query);
        Assert.True(status.HasAuthorization);

        // final image re-fetch went to fal.media WITHOUT auth (SSRF-guarded helper).
        var fetch = handler.Seen.Last();
        Assert.Equal("v3b.fal.media", fetch.Uri!.Host);
        Assert.False(fetch.HasAuthorization);
    }

    [Fact]
    public async Task Result_url_on_disallowed_host_is_rejected()
    {
        var handler = FalHandler("https://evil.example.com/edited.png");
        var svc = new HuggingFaceImageEditService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync(ReferenceImage, "add snow", CancellationToken.None));
    }

    // The HF token must never be sent to a host other than the router: only queue.fal.run URLs are rewritten.
    [Theory]
    [InlineData("https://queue.fal.run/fal-ai/qwen-image-edit/requests/x/status",
                "https://router.huggingface.co/fal-ai/fal-ai/qwen-image-edit/requests/x/status?_subdomain=queue")]
    public void ToRouterUrl_rewrites_queue_urls(string input, string expected) =>
        Assert.Equal(expected, FalQueue.ToRouterUrl(input));

    [Theory]
    [InlineData("https://evil.example.com/requests/x/status")]
    [InlineData("https://queue.fal.run.evil.com/x")]
    [InlineData("http://queue.fal.run/x")]
    public void ToRouterUrl_rejects_non_queue_hosts(string input) =>
        Assert.Throws<InvalidOperationException>(() => FalQueue.ToRouterUrl(input));

    [Fact]
    public void ImageEdit_defaults_match_docs()
    {
        var o = new HuggingFaceOptions();
        Assert.Equal("fal-ai/qwen-image-edit", o.ImageEditModel);
        Assert.Equal("https://router.huggingface.co/fal-ai/{model}?_subdomain=queue", o.ImageEditEndpoint);
        Assert.Equal(120, o.ImageEditTimeoutSeconds);
    }
}
