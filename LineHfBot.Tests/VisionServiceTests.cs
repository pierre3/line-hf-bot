using System.Net;
using System.Text;
using System.Text.Json;
using LineHfBot.Ai;
using LineHfBot.Configuration;
using LineHfBot.State;
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

        var answer = await svc.AnswerAsync(Image, "image/jpeg", [], "what is this?", CancellationToken.None);

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

        await svc.AnswerAsync(Image, "", [], "q", CancellationToken.None);

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

        var answer = await svc.AnswerAsync(Image, "image/png", [], "q", CancellationToken.None);

        Assert.Equal(Messages().EmptyAnswer, answer);
    }

    [Fact]
    public async Task Missing_choices_returns_no_answer_message()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}"));
        var svc = Service(handler);

        var answer = await svc.AnswerAsync(Image, "image/png", [], "q", CancellationToken.None);

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
            () => svc.AnswerAsync(Image, "image/png", [], "q", CancellationToken.None));
    }

    // AC3: with history, the image rides on the first user turn only; later turns are text-only, and the
    // current question is appended last as a text-only user turn.
    [Fact]
    public async Task Multiturn_attaches_image_to_first_user_turn_only()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            "{\"choices\":[{\"message\":{\"content\":\"blue\"}}]}"));
        var svc = Service(handler);
        IReadOnlyList<VisionTurn> history =
        [
            new("what is this?", "a car"),
            new("what brand?", "a Toyota"),
        ];

        await svc.AnswerAsync(Image, "image/jpeg", history, "what color is it?", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Seen[0].Body!);
        var messages = doc.RootElement.GetProperty("messages");
        // user Q1(+image), assistant A1, user Q2, assistant A2, user Q3
        Assert.Equal(5, messages.GetArrayLength());

        // Turn 0: first question carries the image.
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        var t0 = messages[0].GetProperty("content");
        Assert.Equal("what is this?", t0[0].GetProperty("text").GetString());
        Assert.Equal("image_url", t0[1].GetProperty("type").GetString());

        // Turn 1: assistant answer as a plain string.
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("a car", messages[1].GetProperty("content").GetString());

        // Turn 2: second question is text-only (no image part).
        var t2 = messages[2].GetProperty("content");
        Assert.Equal(1, t2.GetArrayLength());
        Assert.Equal("what brand?", t2[0].GetProperty("text").GetString());

        // Last turn: the current question, text-only.
        var last = messages[4].GetProperty("content");
        Assert.Equal("user", messages[4].GetProperty("role").GetString());
        Assert.Equal(1, last.GetArrayLength());
        Assert.Equal("what color is it?", last[0].GetProperty("text").GetString());

        // No message other than the first user turn carries an image.
        for (var i = 1; i < messages.GetArrayLength(); i++)
        {
            var c = messages[i].GetProperty("content");
            if (c.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var part in c.EnumerateArray())
            {
                Assert.NotEqual("image_url", part.GetProperty("type").GetString());
            }
        }
    }

    [Fact]
    public void Vision_defaults_match_docs()
    {
        var o = new HuggingFaceOptions();
        Assert.Equal("Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud", o.VisionModel);
        Assert.Equal("https://router.huggingface.co/v1/chat/completions", o.VisionEndpoint);
        Assert.Equal(120, o.VisionTimeoutSeconds);
        Assert.True(new AppOptions().VisionEnabled);
        Assert.Equal(8, new AppOptions().VisionMaxTurns);
    }
}
