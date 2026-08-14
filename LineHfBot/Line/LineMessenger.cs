using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;

namespace LineHfBot.Line;

/// <summary>Sends messages to LINE users. Reply is free (short-lived token); push is the fallback.</summary>
public interface ILineMessenger
{
    /// <summary>Send a text reply using the reply token. Returns false if it fails (e.g. token expired).</summary>
    Task<bool> TryReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken);

    /// <summary>Push a text message to a user.</summary>
    Task PushTextAsync(string userId, string text, CancellationToken cancellationToken);
}

/// <summary>Thin wrapper over <see cref="MessagingClient"/>.</summary>
public sealed class LineMessenger(MessagingClient client, ILogger<LineMessenger> logger) : ILineMessenger
{
    public async Task<bool> TryReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken)
    {
        try
        {
            await client.Api.V2.Bot.Message.Reply.PostAsync(new ReplyMessageRequest
            {
                ReplyToken = replyToken,
                Messages = [new TextMessage { Text = text }],
            }, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reply failed; will fall back to push.");
            return false;
        }
    }

    public async Task PushTextAsync(string userId, string text, CancellationToken cancellationToken)
    {
        await client.Api.V2.Bot.Message.Push.PostAsync(new PushMessageRequest
        {
            To = userId,
            Messages = [new TextMessage { Text = text }],
        }, cancellationToken: cancellationToken);
    }
}
