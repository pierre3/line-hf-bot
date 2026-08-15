using Line.OpenApi.Messaging.Generated.Api.Models;
using LineHfBot.Text;

namespace LineHfBot.Line;

/// <summary>
/// Builds the QuickReply attached to generation results. Buttons are postback actions
/// (handled in <c>MessageDispatcher</c>) with localized labels. Mode switching itself lives
/// on the rich menu; these are per-result session actions.
/// Kiota discriminators (Type) are set explicitly or LINE rejects the message (400).
/// </summary>
public sealed class QuickReplyFactory(UserMessages messages)
{
    /// <summary>Buttons under an image result: regenerate (same prompt) and back to chat. (Edit arrives in 3b.)</summary>
    public QuickReply ImageResult => new()
    {
        Items =
        [
            Item(messages.LabelRegenerate, "action=regen"),
            Item(messages.LabelBackToChat, "action=mode&value=chat"),
        ],
    };

    /// <summary>Buttons under a video result: back to chat.</summary>
    public QuickReply VideoResult => new()
    {
        Items =
        [
            Item(messages.LabelBackToChat, "action=mode&value=chat"),
        ],
    };

    private static QuickReplyItem Item(string label, string data) => new()
    {
        Type = "action",
        Action = new PostbackAction { Type = "postback", Label = label, Data = data },
    };
}
