using LineHfBot.Ai;

namespace LineHfBot.Tests;

public class MediaRefetchGuardTests
{
    // AC#4: label-boundary matching. "fal.media" allows exact + subdomains, rejects "evilfal.media".
    [Theory]
    [InlineData("fal.media", true)]
    [InlineData("cdn.fal.media", true)]
    [InlineData("a.b.fal.media", true)]
    [InlineData("evilfal.media", false)]
    [InlineData("fal.media.evil.com", false)]
    [InlineData("notfal.media", false)]
    public void IsHostAllowed_matches_on_label_boundary(string host, bool expected)
    {
        string[] allowed = ["fal.media", "replicate.delivery"];
        Assert.Equal(expected, MediaRefetch.IsHostAllowed(host, allowed));
    }

    [Fact]
    public void IsHostAllowed_is_case_insensitive()
    {
        Assert.True(MediaRefetch.IsHostAllowed("CDN.Fal.Media", ["fal.media"]));
    }

    // Empty allowlist denies everything (fail-closed).
    [Theory]
    [InlineData("fal.media")]
    [InlineData("anything.com")]
    public void IsHostAllowed_empty_allowlist_denies_all(string host)
    {
        Assert.False(MediaRefetch.IsHostAllowed(host, []));
    }

    [Theory]
    [InlineData("fal.media;replicate.delivery", new[] { "fal.media", "replicate.delivery" })]
    [InlineData("fal.media, replicate.delivery", new[] { "fal.media", "replicate.delivery" })]
    [InlineData("  fal.media \n replicate.delivery ", new[] { "fal.media", "replicate.delivery" })]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    public void ParseHosts_splits_on_separators(string configured, string[] expected)
    {
        Assert.Equal(expected, MediaRefetch.ParseHosts(configured));
    }
}
