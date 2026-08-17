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
    private static QuickReplyFactory Factory(bool videoEnabled)
    {
        var app = Options.Create(new AppOptions { VideoEnabled = videoEnabled });
        return new QuickReplyFactory(new UserMessages(app), app);
    }

    private static List<string> DataOf(QuickReply qr) =>
        [.. qr.Items!.Select(i => (i.Action as PostbackAction)!.Data!)];

    [Fact]
    public void ImageResult_includes_animate_when_video_enabled()
    {
        var data = DataOf(Factory(videoEnabled: true).ImageResult);
        Assert.Equal(["action=regen", "action=edit", "action=animate", "action=mode&value=chat"], data);
    }

    [Fact]
    public void ImageResult_omits_animate_when_video_disabled()
    {
        var data = DataOf(Factory(videoEnabled: false).ImageResult);
        Assert.Equal(["action=regen", "action=edit", "action=mode&value=chat"], data);
        Assert.DoesNotContain("action=animate", data);
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
