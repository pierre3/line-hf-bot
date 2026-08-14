using LineHfBot.Queue;
using Line.OpenApi.Messaging.Webhook.Generated.Models;

namespace LineHfBot.Messaging;

/// <summary>
/// Webhook で受信したイベントを解析し、コマンド判定して <see cref="IWorkQueue"/> に投入する。
/// v1 はユーザー発の text メッセージのみ対象。満杯時は drop してログ（Push 通知は messaging 増分）。
/// </summary>
public sealed class MessageDispatcher(IWorkQueue queue, ILogger<MessageDispatcher> logger)
{
    public void Dispatch(CallbackRequest callback)
    {
        foreach (var ev in callback.Events ?? [])
        {
            if (ev is not MessageEvent me || me.Message is not TextMessageContent text)
            {
                continue; // v1: text メッセージ以外は無視。
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
                // 満杯 → drop。仕様上は reply で「混雑しています」を通知する（messaging 増分で実装）。
                logger.LogWarning("キュー満杯のため drop: kind={Kind} user={User}", kind, userId);
            }
        }
    }

    /// <summary>先頭のコマンド接頭辞を解釈し、種別と本文（接頭辞除去）に分解する。</summary>
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
