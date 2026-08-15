using Line.OpenApi.Messaging.Generated.Api.Models;

namespace LineHfBot.Line;

/// <summary>
/// Builds the QuickReply buttons attached to bot responses so users can re-trigger
/// the main commands with a tap instead of typing. Each item maps to an existing text
/// command (see <c>MessageDispatcher.ParseCommand</c>).
/// Kiota discriminators (Type) must be set explicitly or LINE rejects the message (400).
/// </summary>
internal static class QuickReplies
{
    /// <summary>Standard action buttons shown after chat and media replies.</summary>
    public static QuickReply Default { get; } = new()
    {
        Items =
        [
            Item("🎨 画像", "/image"),
            Item("🎬 動画", "/video"),
            Item("🗑 リセット", "/reset"),
            Item("❓ 使い方", "/help"),
        ],
    };

    private static QuickReplyItem Item(string label, string text) => new()
    {
        Type = "action",
        Action = new MessageAction { Type = "message", Label = label, Text = text },
    };
}
