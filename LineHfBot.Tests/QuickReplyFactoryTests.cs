using LineHfBot.Configuration;
using LineHfBot.Line;
using LineHfBot.Text;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Microsoft.Extensions.Options;

namespace LineHfBot.Tests;

/// <summary>
/// The "🎬 Make a video" (image-to-video) button is only offered when video is enabled
/// (App:VideoEnabled), on both image results and the received-photo choices.
/// </summary>
public class QuickReplyFactoryTests
{
    private static QuickReplyFactory Factory(bool videoEnabled = false, bool visionEnabled = true)
    {
        var app = Options.Create(new AppOptions { VideoEnabled = videoEnabled, VisionEnabled = visionEnabled });
        return new QuickReplyFactory(new UserMessages(app), app);
    }

    private static List<string> DataOf(QuickReply qr) =>
        [.. qr.Items!.Select(i => (i.Action as PostbackAction)!.Data!)];

    // AC1: order is Regenerate / Edit / Ask (vision) / Animate (video) / Chat.
    [Fact]
    public void ImageResult_includes_ask_and_animate_when_both_enabled()
    {
        var data = DataOf(Factory(videoEnabled: true, visionEnabled: true).ImageResult);
        Assert.Equal(["action=regen", "action=edit", "action=ask", "action=animate", "action=mode&value=chat"], data);
    }

    // AC1: the ask button is present when vision is enabled (video off).
    [Fact]
    public void ImageResult_includes_ask_when_vision_enabled()
    {
        var data = DataOf(Factory(videoEnabled: false, visionEnabled: true).ImageResult);
        Assert.Equal(["action=regen", "action=edit", "action=ask", "action=mode&value=chat"], data);
    }

    // AC1: no ask button when vision is disabled (spec07 behavior for image results).
    [Fact]
    public void ImageResult_omits_ask_when_vision_disabled()
    {
        var data = DataOf(Factory(videoEnabled: false, visionEnabled: false).ImageResult);
        Assert.Equal(["action=regen", "action=edit", "action=mode&value=chat"], data);
        Assert.DoesNotContain("action=ask", data);
    }

    [Fact]
    public void ImageResult_omits_animate_when_video_disabled()
    {
        var data = DataOf(Factory(videoEnabled: false).ImageResult);
        Assert.DoesNotContain("action=animate", data);
    }

    // AC9: the vision answer quick reply is Edit / Animate (video) / Chat — no regenerate, no ask.
    [Fact]
    public void VisionAnswer_includes_animate_when_video_enabled()
    {
        var data = DataOf(Factory(videoEnabled: true).VisionAnswer);
        Assert.Equal(["action=edit", "action=animate", "action=mode&value=chat"], data);
    }

    [Fact]
    public void VisionAnswer_omits_animate_when_video_disabled()
    {
        var data = DataOf(Factory(videoEnabled: false).VisionAnswer);
        Assert.Equal(["action=edit", "action=mode&value=chat"], data);
        Assert.DoesNotContain("action=regen", data);
        Assert.DoesNotContain("action=ask", data);
    }

    [Fact]
    public void ReceivedImageChoices_includes_animate_when_video_enabled()
    {
        var data = DataOf(Factory(videoEnabled: true).ReceivedImageChoices);
        Assert.Equal(["action=edit", "action=ask", "action=animate"], data);
    }

    [Fact]
    public void ReceivedImageChoices_omits_animate_when_video_disabled()
    {
        var data = DataOf(Factory(videoEnabled: false).ReceivedImageChoices);
        Assert.Equal(["action=edit", "action=ask"], data);
    }
}
