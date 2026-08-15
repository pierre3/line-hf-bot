using LineHfBot.Chat;
using LineHfBot.Configuration;
using LineHfBot.Text;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;

namespace LineHfBot.Ai;

/// <summary>Generates a chat reply for a user message, keeping per-user history.</summary>
public interface IChatService
{
    Task<string> CompleteAsync(string userId, string userText, CancellationToken cancellationToken);
}

/// <summary>
/// Chat via Semantic Kernel's chat completion service (backed by the Hugging Face connector).
/// </summary>
public sealed class HuggingFaceChatService(
    IChatCompletionService chat,
    ChatHistoryStore store,
    UserMessages messages,
    IOptions<HuggingFaceOptions> options) : IChatService
{
    public async Task<string> CompleteAsync(string userId, string userText, CancellationToken cancellationToken)
    {
        var history = store.Build(userId, messages.SystemPrompt, userText);

        // Bound the HF call so a slow/cold model does not tie up a worker indefinitely.
        var timeout = Math.Max(5, options.Value.ChatTimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeout));

        string answer;
        try
        {
            var result = await chat.GetChatMessageContentAsync(history, cancellationToken: cts.Token);
            answer = result.Content ?? "";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return messages.Timeout;
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            return messages.EmptyAnswer;
        }

        store.Append(userId, userText, answer);
        return answer;
    }
}
