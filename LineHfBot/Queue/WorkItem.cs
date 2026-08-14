namespace LineHfBot.Queue;

/// <summary>処理種別。ユーザー入力（コマンド/通常テキスト）から判定する。</summary>
public enum WorkKind
{
    Chat,
    Image,
    Video,
    Reset,
    Help,
}

/// <summary>バックグラウンド処理1件分。Webhook イベントから生成される。</summary>
/// <param name="Kind">処理種別</param>
/// <param name="UserId">送信元 LINE userId</param>
/// <param name="ReplyToken">即時 ack 用の reply トークン（短命・一回）</param>
/// <param name="Text">プロンプト本文（コマンド接頭辞を除去済み）</param>
/// <param name="WebhookEventId">冪等性判定用の一意 ID</param>
public sealed record WorkItem(
    WorkKind Kind,
    string UserId,
    string ReplyToken,
    string Text,
    string WebhookEventId);
