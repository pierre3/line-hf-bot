using LineHfBot.Ai;

namespace LineHfBot.Tests;

public class MediaUrlExtractorTests
{
    [Theory]
    [InlineData("{\"url\":\"https://fal.media/a.png\"}", "https://fal.media/a.png")]
    [InlineData("{\"output\":\"https://fal.media/b.png\"}", "https://fal.media/b.png")]
    [InlineData("{\"image\":\"https://fal.media/c.png\"}", "https://fal.media/c.png")]
    [InlineData("{\"images\":[\"https://fal.media/d.png\"]}", "https://fal.media/d.png")]
    [InlineData("{\"images\":[{\"url\":\"https://fal.media/e.png\"}]}", "https://fal.media/e.png")]
    [InlineData("{\"data\":[{\"url\":\"https://fal.media/f.png\"}]}", "https://fal.media/f.png")]
    public void TryExtract_returns_url_for_image_shapes(string json, string expected)
    {
        Assert.Equal(expected, MediaUrlExtractor.TryExtract(json));
    }

    // AC#9: existing video shapes must keep working after unification.
    [Theory]
    [InlineData("{\"video\":\"https://replicate.delivery/v.mp4\"}", "https://replicate.delivery/v.mp4")]
    [InlineData("{\"video\":{\"url\":\"https://replicate.delivery/w.mp4\"}}", "https://replicate.delivery/w.mp4")]
    public void TryExtract_returns_url_for_legacy_video_shapes(string json, string expected)
    {
        Assert.Equal(expected, MediaUrlExtractor.TryExtract(json));
    }

    // AC#6: unextractable JSON yields null so the caller can fail explicitly.
    [Theory]
    [InlineData("{\"foo\":1}")]
    [InlineData("[]")]
    [InlineData("\"just a string\"")]
    [InlineData("{\"images\":[]}")]
    [InlineData("not json at all")]
    public void TryExtract_returns_null_when_no_known_shape(string json)
    {
        Assert.Null(MediaUrlExtractor.TryExtract(json));
    }
}
