using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LineHfBot.Tests;

/// <summary>
/// Guards the /media/{id} range behavior. LINE's inline video player streams via HTTP Range requests
/// (and seeks to a trailing moov atom, common in fal-generated mp4s); without range support the video
/// plays black inline while a full download works. The endpoint returns
/// <c>Results.File(bytes, contentType, enableRangeProcessing: true)</c>; these tests pin that the exact
/// same result answers Range requests with 206 + Accept-Ranges/Content-Range, and a plain GET with 200.
/// </summary>
public class MediaRangeTests
{
    private static readonly byte[] Payload = [.. Enumerable.Range(0, 10).Select(i => (byte)i)];

    private static async Task<DefaultHttpContext> ExecuteAsync(string? rangeHeader)
    {
        // FileContentHttpResult.ExecuteAsync resolves ILoggerFactory from RequestServices.
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        ctx.Request.Method = HttpMethods.Get;
        if (rangeHeader is not null)
        {
            ctx.Request.Headers.Range = rangeHeader;
        }
        ctx.Response.Body = new MemoryStream();

        // Same construction as the /media/{id} endpoint in Program.cs.
        IResult result = Results.File(Payload, "video/mp4", enableRangeProcessing: true);
        await result.ExecuteAsync(ctx);
        return ctx;
    }

    [Fact]
    public async Task RangeRequest_returns_206_with_partial_content()
    {
        var ctx = await ExecuteAsync("bytes=0-3");

        Assert.Equal(StatusCodes.Status206PartialContent, ctx.Response.StatusCode);
        Assert.Equal("bytes", ctx.Response.Headers.AcceptRanges.ToString());
        Assert.StartsWith("bytes 0-3/10", ctx.Response.Headers.ContentRange.ToString());
        Assert.Equal(4, ctx.Response.Body.Length); // bytes 0..3 inclusive
    }

    [Fact]
    public async Task PlainGet_returns_200_and_advertises_range_support()
    {
        var ctx = await ExecuteAsync(rangeHeader: null);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.Equal("bytes", ctx.Response.Headers.AcceptRanges.ToString());
        Assert.Equal(Payload.Length, ctx.Response.Body.Length);
    }
}
