using System.Text;
using System.Text.Json;
using LineHfBot.Ai;
using LineHfBot.Configuration;
using Microsoft.Extensions.Options;

namespace LineHfBot.Tests;

/// <summary>
/// Text-to-video via the fal-ai async queue on the HF router: submit {prompt} → poll status
/// (router-rewritten, HF token) → read video.url → SSRF-guarded re-fetch (fal.media, no auth).
/// </summary>
public class VideoServiceTests
{
    private static readonly byte[] GeneratedVideo = Encoding.UTF8.GetBytes("mp4-fake-bytes");

    private static IOptions<HuggingFaceOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new HuggingFaceOptions
        {
            ApiKey = "hf_test_token",
            VideoModel = "fal-ai/wan/v2.2-5b/text-to-video",
            VideoEndpoint = "https://router.huggingface.co/fal-ai/{model}?_subdomain=queue",
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
        // The result endpoint (fal text-to-video shape: video.url).
        return StubHttpMessageHandler.Json($"{{\"video\":{{\"url\":\"{resultVideoUrl}\"}}}}");
    });

    // AC#1: submit posts {prompt} to the fal endpoint with Bearer auth.
    [Fact]
    public async Task Submit_posts_prompt_with_auth()
    {
        var handler = FalHandler("https://cdn.fal.media/out.mp4");
        var svc = new HuggingFaceVideoService(new HttpClient(handler), Options());

        await svc.GenerateAsync("a running cat", CancellationToken.None);

        var post = handler.Seen[0];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.True(post.HasAuthorization);
        Assert.Equal("https://router.huggingface.co/fal-ai/fal-ai/wan/v2.2-5b/text-to-video?_subdomain=queue", post.Uri!.ToString());

        using var doc = JsonDocument.Parse(post.Body!);
        Assert.Equal("a running cat", doc.RootElement.GetProperty("prompt").GetString());
    }

    // AC#2/#3/#4: poll/result URLs are rewritten to the router host (HF token only goes there),
    // status=COMPLETED yields video.url, and the fal.media video is re-fetched WITHOUT Authorization.
    [Fact]
    public async Task Polls_via_router_and_refetches_video_without_authorization()
    {
        var handler = FalHandler("https://v3b.fal.media/files/out.mp4");
        var svc = new HuggingFaceVideoService(new HttpClient(handler), Options());

        var media = await svc.GenerateAsync("ocean waves", CancellationToken.None);

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
        var svc = new HuggingFaceVideoService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync("a dog", CancellationToken.None));
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
        var svc = new HuggingFaceVideoService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync("a dog", CancellationToken.None));
    }

    // AC#8: defaults match the docs / appsettings.
    [Fact]
    public void Video_defaults_match_docs()
    {
        var o = new HuggingFaceOptions();
        Assert.Equal("fal-ai/wan/v2.2-5b/text-to-video", o.VideoModel);
        Assert.Equal("https://router.huggingface.co/fal-ai/{model}?_subdomain=queue", o.VideoEndpoint);
        Assert.Equal(300, o.VideoTimeoutSeconds);
    }
}
