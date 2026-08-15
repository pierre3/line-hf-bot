using LineHfBot.Line;
using LineHfBot.Queue;
using LineHfBot.State;
using LineHfBot.Text;
using Line.OpenApi.Messaging.Webhook.Generated.Models;

namespace LineHfBot.Messaging;

/// <summary>
/// Parses webhook events and enqueues work. Text messages are interpreted by the user's current
/// mode (chat/image/video); a leading slash command is always an explicit override. Postback events
/// (rich menu taps and result buttons) switch mode or regenerate. When the queue is full, the item
/// is dropped and the user is told (best-effort) that the bot is busy.
/// </summary>
public sealed class MessageDispatcher(
    IWorkQueue queue,
    ILineMessenger messenger,
    UserStateStore userState,
    RichMenuManager richMenu,
    UserMessages messages,
    ILogger<MessageDispatcher> logger)
{
    public async Task DispatchAsync(CallbackRequest callback, CancellationToken cancellationToken)
    {
        foreach (var ev in callback.Events ?? [])
        {
            switch (ev)
            {
                case MessageEvent { Message: TextMessageContent text } me:
                    await HandleTextAsync(me, text, cancellationToken);
                    break;
                case PostbackEvent pe:
                    await HandlePostbackAsync(pe, cancellationToken);
                    break;
                // v1: ignore anything else (non-text messages, follow/join, etc.).
            }
        }
    }

    private async Task HandleTextAsync(MessageEvent me, TextMessageContent text, CancellationToken cancellationToken)
    {
        var userId = (me.Source as UserSource)?.UserId ?? "";
        var replyToken = me.ReplyToken ?? "";
        var eventId = me.WebhookEventId ?? "";
        var raw = (text.Text ?? "").Trim();

        // A leading slash is an explicit command override; it does not change the current mode.
        // Otherwise the message is interpreted by the user's current mode.
        var (kind, body) = raw.StartsWith('/')
            ? ParseCommand(raw)
            : userState.GetMode(userId) switch
            {
                ChatMode.Image => (WorkKind.Image, raw),
                ChatMode.Video => (WorkKind.Video, raw),
                _ => (WorkKind.Chat, raw),
            };

        await EnqueueOrBusyAsync(new WorkItem(kind, userId, replyToken, body, eventId), replyToken, cancellationToken);
    }

    private async Task HandlePostbackAsync(PostbackEvent pe, CancellationToken cancellationToken)
    {
        var userId = (pe.Source as UserSource)?.UserId ?? "";
        var replyToken = pe.ReplyToken ?? "";
        var eventId = pe.WebhookEventId ?? "";
        var data = ParsePostback(pe.Postback?.Data ?? "");
        if (!data.TryGetValue("action", out var action))
        {
            return;
        }

        switch (action)
        {
            case "mode" when data.TryGetValue("value", out var value) && TryParseMode(value, out var mode):
                userState.SetMode(userId, mode);
                await richMenu.SyncUserMenuAsync(userId, mode, cancellationToken);
                logger.LogInformation("mode change: user={User} mode={Mode}", userId, mode);
                if (!string.IsNullOrEmpty(replyToken))
                {
                    await messenger.TryReplyTextAsync(replyToken, ModeAck(mode), cancellationToken);
                }
                break;

            case "regen":
                var snapshot = userState.Get(userId);
                if (!string.IsNullOrWhiteSpace(snapshot.LastPrompt))
                {
                    await EnqueueOrBusyAsync(
                        new WorkItem(WorkKind.Image, userId, replyToken, snapshot.LastPrompt!, eventId),
                        replyToken, cancellationToken);
                }
                else if (!string.IsNullOrEmpty(replyToken))
                {
                    await messenger.TryReplyTextAsync(replyToken, messages.RegenNoImage, cancellationToken);
                }
                break;
        }
    }

    private async Task EnqueueOrBusyAsync(WorkItem item, string replyToken, CancellationToken cancellationToken)
    {
        if (queue.TryEnqueue(item))
        {
            logger.LogInformation("enqueue: kind={Kind} user={User} eventId={EventId}", item.Kind, item.UserId, item.WebhookEventId);
        }
        else
        {
            // Full -> drop. Notify the user now, while the reply token is still usable.
            logger.LogWarning("Queue full, dropped: kind={Kind} user={User}", item.Kind, item.UserId);
            if (!string.IsNullOrEmpty(replyToken))
            {
                await messenger.TryReplyTextAsync(replyToken, messages.Busy, cancellationToken);
            }
        }
    }

    private string ModeAck(ChatMode mode) => mode switch
    {
        ChatMode.Image => messages.ModeImageSet,
        ChatMode.Video => messages.ModeVideoSet,
        _ => messages.ModeChatSet,
    };

    private static bool TryParseMode(string value, out ChatMode mode)
    {
        switch (value)
        {
            case "image": mode = ChatMode.Image; return true;
            case "video": mode = ChatMode.Video; return true;
            case "chat": mode = ChatMode.Chat; return true;
            default: mode = ChatMode.Chat; return false;
        }
    }

    /// <summary>Parse a postback data string like "action=mode&amp;value=image" into a key/value map.</summary>
    internal static Dictionary<string, string> ParsePostback(string data)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in data.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            if (i > 0)
            {
                result[pair[..i]] = pair[(i + 1)..];
            }
        }
        return result;
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
