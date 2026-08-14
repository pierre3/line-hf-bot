using LineHfBot.Queue;
using Line.OpenApi.Messaging.Webhook.Generated.Models;

namespace LineHfBot.Messaging;

/// <summary>
/// Parses webhook events, detects the command, and enqueues work onto <see cref="IWorkQueue"/>.
/// v1 handles user-originated text messages only. When the queue is full, the item is dropped and logged
/// (push notification will be added in the messaging increment).
/// </summary>
public sealed class MessageDispatcher(IWorkQueue queue, ILogger<MessageDispatcher> logger)
{
    public void Dispatch(CallbackRequest callback)
    {
        foreach (var ev in callback.Events ?? [])
        {
            if (ev is not MessageEvent me || me.Message is not TextMessageContent text)
            {
                continue; // v1: ignore anything other than text messages.
            }

            var userId = (me.Source as UserSource)?.UserId ?? "";
            var replyToken = me.ReplyToken ?? "";
            var eventId = me.WebhookEventId ?? "";
            var raw = text.Text ?? "";

            var (kind, body) = ParseCommand(raw);
            var item = new WorkItem(kind, userId, replyToken, body, eventId);

            if (queue.TryEnqueue(item))
            {
                logger.LogInformation("enqueue: kind={Kind} user={User} eventId={EventId}", kind, userId, eventId);
            }
            else
            {
                // Full -> drop. The spec calls for a "bot is busy" reply (added in the messaging increment).
                logger.LogWarning("Queue full, dropped: kind={Kind} user={User}", kind, userId);
            }
        }
    }

    /// <summary>Interpret a leading command prefix and split into kind and body (prefix stripped).</summary>
    internal static (WorkKind Kind, string Body) ParseCommand(string raw)
    {
        var t = raw.TrimStart();

        if (TryPrefix(t, "/image", out var imgArg)) return (WorkKind.Image, imgArg);
        if (TryPrefix(t, "/video", out var vidArg)) return (WorkKind.Video, vidArg);
        if (t.Equals("/reset", StringComparison.OrdinalIgnoreCase)) return (WorkKind.Reset, "");
        if (t.Equals("/help", StringComparison.OrdinalIgnoreCase)) return (WorkKind.Help, "");

        return (WorkKind.Chat, raw);
    }

    private static bool TryPrefix(string text, string command, out string arg)
    {
        if (text.Equals(command, StringComparison.OrdinalIgnoreCase))
        {
            arg = "";
            return true;
        }
        if (text.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase))
        {
            arg = text[(command.Length + 1)..].Trim();
            return true;
        }
        arg = "";
        return false;
    }
}
