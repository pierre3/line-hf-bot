using System.Net.Http.Headers;
using System.Text;
using LineHfBot.Ai;
using LineHfBot.Configuration;
using Microsoft.Extensions.Options;

namespace LineHfBot.Tests;

/// <summary>End-to-end response handling for the image/video services using a stub HTTP handler.</summary>
public class MediaServiceTests
{
    private static readonly byte[] SamplePng = Encoding.UTF8.GetBytes("PNG-fake-bytes");
    private static readonly byte[] SampleMp4 = Encoding.UTF8.GetBytes("mp4-fake-bytes");

    private static IOptions<HuggingFaceOptions> Options(string allowedHosts = "fal.media;replicate.delivery") =>
        Microsoft.Extensions.Options.Options.Create(new HuggingFaceOptions
        {
            ApiKey = "hf_test_token",
            ImageModel = "some/model",
            VideoModel = "some/video-model",
            ImageEndpoint = "https://router.huggingface.co/hf-inference/models/{model}",
            VideoEndpoint = "https://router.huggingface.co/hf-inference/models/{model}",
            MediaRefetchAllowedHosts = allowedHosts,
            ImageTimeoutSeconds = 30,
            VideoTimeoutSeconds = 30,
        });

    // AC#1: raw-bytes response is returned directly (no re-fetch).
    [Fact]
    public async Task Image_raw_bytes_response_returns_bytes()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(SamplePng, "image/png"));
        var svc = new HuggingFaceImageService(new HttpClient(handler), Options());

        var media = await svc.GenerateAsync("a dog", CancellationToken.None);

        Assert.Equal(SamplePng, media.Bytes);
        Assert.Equal("image/png", media.ContentType);
        Assert.Single(handler.Seen); // only the POST; no re-fetch
    }

    // AC#2: JSON-URL response is extracted and re-fetched, then served as bytes.
    [Fact]
    public async Task Image_json_url_response_is_refetched()
    {
        var handler = new StubHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? StubHttpMessageHandler.Json("{\"images\":[{\"url\":\"https://cdn.fal.media/out.png\"}]}")
            : StubHttpMessageHandler.Bytes(SamplePng, "image/png"));
        var svc = new HuggingFaceImageService(new HttpClient(handler), Options());

        var media = await svc.GenerateAsync("a dog", CancellationToken.None);

        Assert.Equal(SamplePng, media.Bytes);
        Assert.Equal(2, handler.Seen.Count);
        Assert.Equal(HttpMethod.Post, handler.Seen[0].Method);
        Assert.Equal(HttpMethod.Get, handler.Seen[1].Method);
        Assert.Equal("cdn.fal.media", handler.Seen[1].Uri!.Host);
    }

    // AC#7: the re-fetch GET must NOT carry the HF Authorization header.
    [Fact]
    public async Task Refetch_get_carries_no_authorization()
    {
        var handler = new StubHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? StubHttpMessageHandler.Json("{\"url\":\"https://fal.media/out.png\"}")
            : StubHttpMessageHandler.Bytes(SamplePng, "image/png"));
        var svc = new HuggingFaceImageService(new HttpClient(handler), Options());

        await svc.GenerateAsync("a dog", CancellationToken.None);

        Assert.True(handler.Seen[0].HasAuthorization);   // POST to HF is authenticated
        Assert.False(handler.Seen[1].HasAuthorization);  // re-fetch GET is not
    }

    // AC#3: a URL on a non-allowlisted host is rejected without re-fetching.
    [Fact]
    public async Task Image_rejects_host_outside_allowlist()
    {
        var handler = new StubHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? StubHttpMessageHandler.Json("{\"url\":\"https://evil.example.com/out.png\"}")
            : StubHttpMessageHandler.Bytes(SamplePng, "image/png"));
        var svc = new HuggingFaceImageService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync("a dog", CancellationToken.None));
        Assert.Single(handler.Seen); // POST only; no GET re-fetch happened
    }

    // AC#4: label-boundary bypass attempt is rejected end-to-end.
    [Fact]
    public async Task Image_rejects_label_boundary_bypass_host()
    {
        var handler = new StubHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? StubHttpMessageHandler.Json("{\"url\":\"https://evilfal.media/out.png\"}")
            : StubHttpMessageHandler.Bytes(SamplePng, "image/png"));
        var svc = new HuggingFaceImageService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync("a dog", CancellationToken.None));
        Assert.Single(handler.Seen);
    }

    // AC#5: non-https URL is rejected.
    [Fact]
    public async Task Image_rejects_non_https_url()
    {
        var handler = new StubHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? StubHttpMessageHandler.Json("{\"url\":\"http://fal.media/out.png\"}")
            : StubHttpMessageHandler.Bytes(SamplePng, "image/png"));
        var svc = new HuggingFaceImageService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync("a dog", CancellationToken.None));
        Assert.Single(handler.Seen);
    }

    // AC#6: unextractable JSON fails explicitly.
    [Fact]
    public async Task Image_unextractable_json_throws()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{\"status\":\"queued\"}"));
        var svc = new HuggingFaceImageService(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateAsync("a dog", CancellationToken.None));
    }

    // AC#9: video JSON-URL response (video.url shape) still extracts + re-fetches.
    [Fact]
    public async Task Video_json_url_response_is_refetched()
    {
        var handler = new StubHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? StubHttpMessageHandler.Json("{\"video\":{\"url\":\"https://replicate.delivery/out.mp4\"}}")
            : StubHttpMessageHandler.Bytes(SampleMp4, "video/mp4"));
        var svc = new HuggingFaceVideoService(new HttpClient(handler), Options());

        var media = await svc.GenerateAsync("a running dog", CancellationToken.None);

        Assert.Equal(SampleMp4, media.Bytes);
        Assert.Equal("video/mp4", media.ContentType);
        Assert.Equal("replicate.delivery", handler.Seen[1].Uri!.Host);
        Assert.False(handler.Seen[1].HasAuthorization);
    }

    // AC#10: the new config key exists with the documented default.
    [Fact]
    public void MediaRefetchAllowedHosts_default_matches_docs()
    {
        Assert.Equal("fal.media;replicate.delivery", new HuggingFaceOptions().MediaRefetchAllowedHosts);
    }
}
