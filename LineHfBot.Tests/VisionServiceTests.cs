using System.Net;
using System.Text;
using System.Text.Json;
using LineHfBot.Ai;
using LineHfBot.Configuration;
using LineHfBot.Text;
using Microsoft.Extensions.Options;

namespace LineHfBot.Tests;

public class VisionServiceTests
{
    private static readonly byte[] Image = Encoding.UTF8.GetBytes("photo-bytes");

    private static IOptions<HuggingFaceOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new HuggingFaceOptions
        {
            ApiKey = "hf_test_token",
            VisionModel = "Qwen/Qwen2.5-VL-7B-Instruct",
            VisionEndpoint = "https://router.huggingface.co/v1/chat/completions",
            VisionTimeoutSeconds = 30,
        });

    private static UserMessages Messages() =>
        new(Microsoft.Extensions.Options.Options.Create(new AppOptions()));

    private static HuggingFaceVisionService Service(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler), Options(), Messages());

    [Fact]
    public async Task Posts_model_and_multimodal_content_with_auth()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"a cat\"}}]}"));
        var svc = Service(handler);

        var answer = await svc.AnswerAsync(Image, "image/jpeg", "what is this?", CancellationToken.None);

        Assert.Equal("a cat", answer);

        var post = handler.Seen[0];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.True(post.HasAuthorization);
        Assert.Equal("https://router.huggingface.co/v1/chat/completions", post.Uri!.ToString());

        using var doc = JsonDocument.Parse(post.Body!);
        var root = doc.RootElement;
        Assert.Equal("Qwen/Qwen2.5-VL-7B-Instruct", root.GetProperty("model").GetString());
        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("what is this?", content[0].GetProperty("text").GetString());
        Assert.Equal("image_url", content[1].GetProperty("type").GetString());
        Assert.Equal(
            $"data:image/jpeg;base64,{Convert.ToBase64String(Image)}",
            content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Unknown_media_type_defaults_to_png_data_uri()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
        var svc = Service(handler);

        await svc.AnswerAsync(Image, "", "q", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Seen[0].Body!);
        var url = doc.RootElement.GetProperty("messages")[0].GetProperty("content")[1]
            .GetProperty("image_url").GetProperty("url").GetString();
        Assert.StartsWith("data:image/png;base64,", url);
    }

    [Fact]
    public async Task Empty_content_returns_no_answer_message()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            "{\"choices\":[{\"message\":{\"content\":\"\"}}]}"));
        var svc = Service(handler);

        var answer = await svc.AnswerAsync(Image, "image/png", "q", CancellationToken.None);

        Assert.Equal(Messages().EmptyAnswer, answer);
    }

    [Fact]
    public async Task Missing_choices_returns_no_answer_message()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}"));
        var svc = Service(handler);

        var answer = await svc.AnswerAsync(Image, "image/png", "q", CancellationToken.None);

        Assert.Equal(Messages().EmptyAnswer, answer);
    }

    // Non-2xx is surfaced as an exception (the worker's top-level handler turns it into the generic error).
    [Fact]
    public async Task Non_success_status_throws()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("model unavailable"),
        });
        var svc = Service(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.AnswerAsync(Image, "image/png", "q", CancellationToken.None));
    }

    [Fact]
    public void Vision_defaults_match_docs()
    {
        var o = new HuggingFaceOptions();
        Assert.Equal("Qwen/Qwen2.5-VL-7B-Instruct", o.VisionModel);
        Assert.Equal("https://router.huggingface.co/v1/chat/completions", o.VisionEndpoint);
        Assert.Equal(60, o.VisionTimeoutSeconds);
        Assert.True(new AppOptions().VisionEnabled);
    }
}
