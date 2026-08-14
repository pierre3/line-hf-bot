using System.Collections.Concurrent;
using LineHfBot.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;

namespace LineHfBot.Chat;

/// <summary>
/// Per-user conversation history kept in memory, capped at a configurable number of turns.
/// One "turn" is a user message plus the assistant reply. Thread-safe for concurrent workers.
/// </summary>
public sealed class ChatHistoryStore(IOptions<ChatOptions> options)
{
    private readonly int _maxTurns = Math.Max(1, options.Value.MaxHistory);
    private readonly ConcurrentDictionary<string, List<(string User, string Assistant)>> _byUser = new();

    /// <summary>Build a SK ChatHistory: system prompt, prior turns, then the new user message.</summary>
    public ChatHistory Build(string userId, string systemPrompt, string newUserText)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);

        if (_byUser.TryGetValue(userId, out var turns))
        {
            lock (turns)
            {
                foreach (var (user, assistant) in turns)
                {
                    history.AddUserMessage(user);
                    history.AddAssistantMessage(assistant);
                }
            }
        }

        history.AddUserMessage(newUserText);
        return history;
    }

    /// <summary>Append a completed turn, trimming the oldest turns beyond the cap.</summary>
    public void Append(string userId, string userText, string assistantText)
    {
        var turns = _byUser.GetOrAdd(userId, static _ => []);
        lock (turns)
        {
            turns.Add((userText, assistantText));
            if (turns.Count > _maxTurns)
            {
                turns.RemoveRange(0, turns.Count - _maxTurns);
            }
        }
    }

    /// <summary>Clear a user's history.</summary>
    public void Reset(string userId) => _byUser.TryRemove(userId, out _);
}
