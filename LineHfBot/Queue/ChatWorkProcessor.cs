using LineHfBot.Ai;
using LineHfBot.Chat;
using LineHfBot.Line;
using LineHfBot.Text;

namespace LineHfBot.Queue;

/// <summary>
/// Handles a dequeued item: chat via HF, plus /reset and /help. Image/video are placeholders
/// until their increments land. Replies (free) when possible, falling back to push.
/// </summary>
public sealed class ChatWorkProcessor(
    IChatService chat,
    ChatHistoryStore history,
    ILineMessenger messenger,
    ILogger<ChatWorkProcessor> logger) : IWorkProcessor
{
    public async Task ProcessAsync(WorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            var reply = item.Kind switch
            {
                WorkKind.Reset => ResetHistory(item.UserId),
                WorkKind.Help => UserMessages.Help,
                WorkKind.Chat => await chat.CompleteAsync(item.UserId, item.Text, cancellationToken),
                WorkKind.Image or WorkKind.Video => UserMessages.NotYetImplemented,
                _ => UserMessages.Error,
            };

            await SendAsync(item, reply, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle item kind={Kind} user={User}", item.Kind, item.UserId);
            // Best-effort notification; ignore secondary failures.
            try { await SendAsync(item, UserMessages.Error, cancellationToken); } catch { /* ignore */ }
        }
    }

    private string ResetHistory(string userId)
    {
        history.Reset(userId);
        return UserMessages.ResetDone;
    }

    private async Task SendAsync(WorkItem item, string text, CancellationToken cancellationToken)
    {
        // Prefer reply (free); fall back to push if the reply token is unusable.
        if (!string.IsNullOrEmpty(item.ReplyToken) &&
            await messenger.TryReplyTextAsync(item.ReplyToken, text, cancellationToken))
        {
            return;
        }

        if (!string.IsNullOrEmpty(item.UserId))
        {
            await messenger.PushTextAsync(item.UserId, text, cancellationToken);
        }
    }
}
