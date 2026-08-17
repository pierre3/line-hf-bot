using System.Text;
using System.Text.Json;
using LineHfBot.Ai;
using LineHfBot.Configuration;
using Microsoft.Extensions.Options;

namespace LineHfBot.Tests;

/// <summary>
/// Image-to-video via the fal-ai async queue on the HF router: submit {image_url (base64 data URI), prompt}
/// → poll status (router-rewritten, HF token) → read video.url → SSRF-guarded re-fetch (fal.media, no auth).
/// Combines the image-edit data-URI reference with the text-to-video result shape.
/// </summary>
public class ImageToVideoServiceTests
{
    private static readonly byte[] ReferenceImage = Encoding.UTF8.GetBytes("reference-image-bytes");
    private static readonly byte[] GeneratedVideo = Encoding.UTF8.GetBytes("mp4-fake-bytes");

    private static IOptions<HuggingFaceOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new HuggingFaceOptions
        {
            ApiKey = "hf_test_token",
            ImageToVideoModel = "fal-ai/wan/v2.2-a14b/image-to-video",
            ImageToVideoEndpoint = "https://router.huggingface.co/fal-ai/{model}?_subdomain=queue",
            MediaRefetchAllowedHosts = "fal.media;replicate.delivery",
            VideoTimeoutSeconds = 30,
        });

    // fal returns queue.fal.run status/response URLs; a submit response fixture with a given result URL.
    private static StubHttpMessageHandler FalHandler(string resultVideoUrl) => new(req =>
    {
        if (req.Method == HttpMethod.Post)
        {
            return StubHttpMessageHandler.Json(
                "{\"status\":\"IN_QUEUE\"," +
                "\"status_url\":\"https://queue.fal.run/fal-ai/wan/requests/req-1/status\"," +
                "\"response_url\":\"https://queue.fal.run/fal-ai/wan/requests/req-1\"}");
        }
        var path = req.RequestUri!.AbsolutePath;
        if (path.EndsWith("/status", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Json("{\"status\":\"COMPLETED\"}");
        }
        if (req.RequestUri!.Host.EndsWith("fal.media", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Bytes(GeneratedVideo, "video/mp4");
        }
        // The result endpoint (fal image-to-video shape: video.url, same as text-to-video).
        return StubHttpMessageHandler.Json($"{{\"video\":{{\"url\":\"{resultVideoUrl}\"}}}}");
    });

    // A minimal PNG header (signature + IHDR) carrying the given dimensions — enough for ImageDimensions.
    private static byte[] Png(int width, int height)
    {
        var b = new byte[24];
        b[0] = 0x89; b[1] = 0x50; b[2] = 0x4E; b[3] = 0x47; b[4] = 0x0D; b[5] = 0x0A; b[6] = 0x1A; b[7] = 0x0A;
        b[11] = 0x0D; // IHDR length = 13
        b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        b[16] = (byte)(width >> 24); b[17] = (byte)(width >> 16); b[18] = (byte)(width >> 8); b[19] = (byte)width;
        b[20] = (byte)(height >> 24); b[21] = (byte)(height >> 16); b[22] = (byte)(height >> 8); b[23] = (byte)height;
        return b;
    }

    // AC#1: submit posts {image_url (data URI), prompt, aspect_ratio} to the i2v endpoint with Bearer auth.
    [Fact]
    public async Task Submit_posts_image_url_and_prompt_with_auth()
    {
        var handler = FalHandler("https://cdn.fal.media/out.mp4");
        var svc = new HuggingFaceImageToVideoService(new HttpClient(handler), Options());

        await svc.GenerateAsync(ReferenceImage, "image/png", "slowly zoom in", CancellationToken.None);

        var post = handler.Seen[0];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.True(post.HasAuthorization);
        Assert.Equal("https://router.huggingface.co/fal-ai/fal-ai/wan/v2.2-a14b/image-to-video?_subdomain=queue", post.Uri!.ToString());

        using var doc = JsonDocument.Parse(post.Body!);
        var root = doc.RootElement;
        Assert.Equal("slowly zoom in", root.GetProperty("prompt").GetString());
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(ReferenceImage)}", root.GetProperty("image_url").GetString());
        // Non-image bytes → dimensions unknown → safe square fallback.
        Assert.Equal("1:1", root.GetProperty("aspect_ratio").GetString());
    }

    // The output aspect ratio is the supported ratio closest to the input image's own dimensions,
    // so 'auto' (which fails with HF 422 for unsupported resolved sizes) is never sent.
    [Theory]
    [InlineData(816, 1104, "9:16")]  // portrait photo (the size that triggered the original 422)
    [InlineData(1920, 1080, "16:9")] // landscape
    [InlineData(1024, 1024, "1:1")]  // square
    public async Task Submit_maps_input_dimensions_to_supported_aspect_ratio(int w, int h, string expected)
    {
        var handler = FalHandler("https://cdn.fal.media/out.mp4");
        var svc = new HuggingFaceImageToVideoService(new HttpClient(handler), Options());

        await svc.GenerateAsync(Png(w, h), "image/png", "pan", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Seen[0].Body!);
        Assert.Equal(expected, doc.RootElement.GetProperty("aspect_ratio").GetString());
    }

    // The reference content type drives the data URI mime; an empty type falls back to image/png.
    [Fact]
    public async Task Empty_content_type_falls_back_to_png()
    {
        var handler = FalHandler("https://cdn.fal.media/out.mp4");
        var svc = new HuggingFaceImageToVideoService(new HttpClient(handler), Options());

        await svc.GenerateAsync(ReferenceImage, "", "pan left", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Seen[0].Body!);
        Assert.StartsWith("data:image/png;base64,", doc.RootElement.GetProperty("image_url").GetString());
    }

    [Fact]
    public async Task Jpeg_content_type_is_used_in_data_uri()
    {
        var handler = FalHandler("https://cdn.fal.media/out.mp4");
        var svc = new HuggingFaceImageToVideoService(new HttpClient(handler), Options());

        await svc.GenerateAsync(ReferenceImage, "image/jpeg", "pan left", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Seen[0].Body!);
        Assert.StartsWith("data:image/jpeg;base64,", doc.RootElement.GetProperty("image_url").GetString());
    }

    // AC#2/#3/#4: poll/result URLs are rewritten to the router host (HF token only goes there),
    // status=COMPLETED yields video.url, and the fal.media video is re-fetched WITHOUT Authorization.
    [Fact]
    public async Task Polls_via_router_and_refetches_video_without_authorization()
    {
        var handler = FalHandler("https://v3b.fal.media/files/out.mp4");
        var svc = new HuggingFaceImageToVideoService(new HttpClient(handler), Options());

        var media = await svc.GenerateAsync(ReferenceImage, "image/png", "gentle breeze", CancellationToken.None);

        Assert.Equal(GeneratedVideo, media.Bytes);
        Assert.Equal("video/mp4", media.ContentType);

        // status poll went to the router host with auth.
        var status = handler.Seen.First(s => s.Uri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal));
        Assert.Equal("router.huggingface.co", status.Uri!.Host);
        Assert.Contains("_subdomain=queue", status.Uri!.Query);
        Assert.True(status.HasAuthorization);

        // final video re-fetch went to fal.media WITHOUT auth (SSRF-guarded helper).
        var fetch = handler.Seen.Last();
        Assert.Equal("v3b.fal.media", fetch.Uri!.Host);
        Assert.False(fetch.HasAuthorization);
    }

    // AC#5: a result URL on a non-allowlisted host is rejected.
    [Fact]
    public async Task Result_url_on_disallowed_host_is_rejected()
    {
        var handler = FalHandler("https://evil.example.com/out.mp4");
        var svc = new HuggingFaceImageToVideoService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync(ReferenceImage, "image/png", "spin", CancellationToken.None));
    }

    // The result document must contain video.url; anything else fails explicitly.
    [Fact]
    public async Task Result_without_video_url_throws()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post)
            {
                return StubHttpMessageHandler.Json(
                    "{\"status\":\"IN_QUEUE\"," +
                    "\"status_url\":\"https://queue.fal.run/fal-ai/wan/requests/x/status\"," +
                    "\"response_url\":\"https://queue.fal.run/fal-ai/wan/requests/x\"}");
            }
            var path = req.RequestUri!.AbsolutePath;
            return path.EndsWith("/status", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json("{\"status\":\"COMPLETED\"}")
                : StubHttpMessageHandler.Json("{\"images\":[{\"url\":\"https://cdn.fal.media/wrong.png\"}]}");
        });
        var svc = new HuggingFaceImageToVideoService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync(ReferenceImage, "image/png", "spin", CancellationToken.None));
    }

    // AC#8: defaults match the docs / appsettings.
    [Fact]
    public void ImageToVideo_defaults_match_docs()
    {
        var o = new HuggingFaceOptions();
        Assert.Equal("fal-ai/wan/v2.2-a14b/image-to-video", o.ImageToVideoModel);
        Assert.Equal("https://router.huggingface.co/fal-ai/{model}?_subdomain=queue", o.ImageToVideoEndpoint);
    }
}
