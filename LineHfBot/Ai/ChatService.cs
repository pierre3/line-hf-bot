using LineHfBot.Chat;
using LineHfBot.Text;
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
    ChatHistoryStore store) : IChatService
{
    // Assistant persona. Product content shown indirectly to users, so kept in Japanese.
    private const string SystemPrompt = "あなたは親切で丁寧な日本語のアシスタントです。分かりやすく簡潔に答えてください。";

    public async Task<string> CompleteAsync(string userId, string userText, CancellationToken cancellationToken)
    {
        var history = store.Build(userId, SystemPrompt, userText);

        var result = await chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
        var answer = result.Content ?? "";

        if (string.IsNullOrWhiteSpace(answer))
        {
            return UserMessages.EmptyAnswer;
        }

        store.Append(userId, userText, answer);
        return answer;
    }
}
