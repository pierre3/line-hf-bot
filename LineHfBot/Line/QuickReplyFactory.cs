using Line.OpenApi.Messaging.Generated.Api.Models;
using LineHfBot.Configuration;
using LineHfBot.Text;
using Microsoft.Extensions.Options;

namespace LineHfBot.Line;

/// <summary>
/// Builds the QuickReply attached to generation results. Buttons are postback actions
/// (handled in <c>MessageDispatcher</c>) with localized labels. Mode switching itself lives
/// on the rich menu; these are per-result session actions. The "🎬 Make a video" (image-to-video)
/// button is only offered when video is enabled (<see cref="AppOptions.VideoEnabled"/>), since it runs
/// on the credit-heavy fal provider.
/// Kiota discriminators (Type) are set explicitly or LINE rejects the message (400).
/// </summary>
public sealed class QuickReplyFactory(UserMessages messages, IOptions<AppOptions> appOptions)
{
    private bool VideoEnabled => appOptions.Value.VideoEnabled;
    private bool VisionEnabled => appOptions.Value.VisionEnabled;

    /// <summary>Buttons under an image result: regenerate (same prompt), edit (image-to-image),
    /// ask (vision Q&amp;A, when vision is enabled), animate (image-to-video, when video is enabled),
    /// back to chat.</summary>
    public QuickReply ImageResult
    {
        get
        {
            List<QuickReplyItem> items =
            [
                Item(messages.LabelRegenerate, "action=regen"),
                Item(messages.LabelEdit, "action=edit"),
            ];
            if (VisionEnabled)
            {
                items.Add(Item(messages.LabelAsk, "action=ask"));
            }
            if (VideoEnabled)
            {
                items.Add(Item(messages.LabelAnimate, "action=animate"));
            }
            items.Add(Item(messages.LabelBackToChat, "action=mode&value=chat"));
            return new QuickReply { Items = items };
        }
    }

    /// <summary>Buttons under a conversational vision answer (spec09): edit the image, animate it
    /// (image-to-video, when video is enabled), or leave the session for chat. Follow-up questions
    /// need no button — a plain message continues the session.</summary>
    public QuickReply VisionAnswer
    {
        get
        {
            List<QuickReplyItem> items =
            [
                Item(messages.LabelEdit, "action=edit"),
            ];
            if (VideoEnabled)
            {
                items.Add(Item(messages.LabelAnimate, "action=animate"));
            }
            items.Add(Item(messages.LabelBackToChat, "action=mode&value=chat"));
            return new QuickReply { Items = items };
        }
    }

    /// <summary>Buttons offered when a user sends a photo (vision enabled): edit it, ask about it,
    /// or animate it (image-to-video, when video is enabled).</summary>
    public QuickReply ReceivedImageChoices
    {
        get
        {
            List<QuickReplyItem> items =
            [
                Item(messages.LabelEdit, "action=edit"),
                Item(messages.LabelAsk, "action=ask"),
            ];
            if (VideoEnabled)
            {
                items.Add(Item(messages.LabelAnimate, "action=animate"));
            }
            return new QuickReply { Items = items };
        }
    }

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
