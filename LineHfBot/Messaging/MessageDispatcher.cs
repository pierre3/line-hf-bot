using LineHfBot.Line;
using LineHfBot.Queue;
using LineHfBot.Text;
using Line.OpenApi.Messaging.Webhook.Generated.Models;

namespace LineHfBot.Messaging;

/// <summary>
/// Parses webhook events, detects the command, and enqueues work onto <see cref="IWorkQueue"/>.
/// v1 handles user-originated text messages only. When the queue is full, the item is dropped and
/// the user is told (best-effort) that the bot is busy.
/// </summary>
public sealed class MessageDispatcher(IWorkQueue queue, ILineMessenger messenger, ILogger<MessageDispatcher> logger)
{
    public async Task DispatchAsync(CallbackRequest callback, CancellationToken cancellationToken)
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
                // Full -> drop. Notify the user now, while the reply token is still usable.
                logger.LogWarning("Queue full, dropped: kind={Kind} user={User}", kind, userId);
                if (!string.IsNullOrEmpty(replyToken))
                {
                    await messenger.TryReplyTextAsync(replyToken, UserMessages.Busy, cancellationToken);
                }
            }
        }
    }

    /// <summary>Interpret a leading command prefix and split into kind and body (prefix stripped, trimmed).</summary>
    internal static (WorkKind Kind, string Body) ParseCommand(string raw)
    {
        var t = raw.Trim();

        if (TryPrefix(t, "/image", out var imgArg)) return (WorkKind.Image, imgArg);
        if (TryPrefix(t, "/video", out var vidArg)) return (WorkKind.Video, vidArg);
        if (t.Equals("/reset", StringComparison.OrdinalIgnoreCase)) return (WorkKind.Reset, "");
        if (t.Equals("/help", StringComparison.OrdinalIgnoreCase)) return (WorkKind.Help, "");

        return (WorkKind.Chat, t);
    }

    private static bool TryPrefix(string text, string command, out string arg)
    {
        arg = "";
        if (!text.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (text.Length == command.Length)
        {
            return true; // exactly the command, no argument
        }
        // The separator after the command must be whitespace so "/imagex" is not treated as "/image".
        // Accept any Unicode whitespace (e.g. the full-width space U+3000 a Japanese IME often inserts),
        // not just an ASCII space; Trim() then strips it along with any extra leading/trailing whitespace.
        if (!char.IsWhiteSpace(text[command.Length]))
        {
            return false;
        }
        arg = text[command.Length..].Trim();
        return true;
    }
}
