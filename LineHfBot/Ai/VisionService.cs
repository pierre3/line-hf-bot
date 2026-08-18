using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LineHfBot.Configuration;
using LineHfBot.State;
using LineHfBot.Text;
using Microsoft.Extensions.Options;

namespace LineHfBot.Ai;

/// <summary>Answers a text question about a user-sent image (vision / VQA), optionally as a follow-up
/// in a conversational session (spec09).</summary>
public interface IVisionService
{
    /// <summary>
    /// Returns a display-ready answer string. <paramref name="history"/> is the prior Q&amp;A in this vision
    /// session (empty for the first turn); the endpoint is stateless, so the full conversation is resent each
    /// time with the image attached to the first user turn only. On the internal timeout it returns the localized
    /// "timeout" message and on an empty model reply the "no answer" message (same contract as
    /// <see cref="HuggingFaceChatService"/>). Non-2xx responses throw so the caller's top-level
    /// handler can notify the user with the generic error.
    /// </summary>
    Task<string> AnswerAsync(byte[] image, string mediaType, IReadOnlyList<VisionTurn> history, string question, CancellationToken cancellationToken);
}

/// <summary>
/// Vision question answering via the OpenAI-compatible chat completions endpoint on the Hugging Face
/// router (default model Qwen/Qwen2.5-VL-7B-Instruct). The image rides as a base64 data URI in an
/// <c>image_url</c> content part, so no hosting is needed. The SK Hugging Face connector's multimodal
/// support is uncertain, so this calls the endpoint directly (like the image/video services).
/// Error handling mirrors <see cref="HuggingFaceChatService"/>: the service converts the internal
/// timeout (OCE) and an empty reply into display strings, because the worker's top-level catch excludes
/// OperationCanceledException and would otherwise leave the user without a reply.
/// </summary>
public sealed class HuggingFaceVisionService(
    HttpClient http,
    IOptions<HuggingFaceOptions> options,
    UserMessages messages) : IVisionService
{
    public async Task<string> AnswerAsync(byte[] image, string mediaType, IReadOnlyList<VisionTurn> history, string question, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opt.VisionTimeoutSeconds)));

        var mime = string.IsNullOrWhiteSpace(mediaType) ? "image/png" : mediaType;
        var dataUri = $"data:{mime};base64,{Convert.ToBase64String(image)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, opt.VisionEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = opt.VisionModel,
            messages = BuildMessages(history, question, dataUri),
        });

        try
        {
            using var response = await http.SendAsync(request, cts.Token);
            await HfHttp.EnsureSuccessAsync(response, cts.Token);

            using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cts.Token)
                ?? throw new InvalidOperationException("Vision response body was empty.");
            var answer = ExtractContent(doc.RootElement);
            return string.IsNullOrWhiteSpace(answer) ? messages.EmptyAnswer : answer;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return messages.Timeout;
        }
    }

    /// <summary>
    /// Build the OpenAI-compatible messages array for a (possibly multi-turn) vision conversation. The image
    /// is attached to the first user turn only; the current <paramref name="question"/> is appended last as a
    /// text-only user turn. When <paramref name="history"/> is empty the image rides with the current question
    /// (the standard single-turn shape).
    /// </summary>
    private static List<object> BuildMessages(IReadOnlyList<VisionTurn> history, string question, string dataUri)
    {
        object TextPart(string text) => new { type = "text", text };
        object ImagePart() => new { type = "image_url", image_url = new { url = dataUri } };

        var messages = new List<object>();
        if (history.Count == 0)
        {
            messages.Add(new { role = "user", content = new[] { TextPart(question), ImagePart() } });
            return messages;
        }

        for (var i = 0; i < history.Count; i++)
        {
            var content = i == 0
                ? new[] { TextPart(history[i].Question), ImagePart() } // image on the first user turn only
                : [TextPart(history[i].Question)];
            messages.Add(new { role = "user", content });
            messages.Add(new { role = "assistant", content = history[i].Answer });
        }
        messages.Add(new { role = "user", content = new[] { TextPart(question) } });
        return messages;
    }

    private static string? ExtractContent(JsonElement root) =>
        root.TryGetProperty("choices", out var choices) &&
        choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
        choices[0].TryGetProperty("message", out var message) &&
        message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
            ? content.GetString()
            : null;
}
