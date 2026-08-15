using LineHfBot.Line;

namespace LineHfBot.Tests;

public class LineContentServiceTests
{
    [Fact]
    public async Task ReadCappedAsync_returns_all_bytes_under_cap()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(data);

        var result = await LineContentService.ReadCappedAsync(stream, maxBytes: 10, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task ReadCappedAsync_returns_null_when_over_cap()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var stream = new MemoryStream(data);

        var result = await LineContentService.ReadCappedAsync(stream, maxBytes: 4, CancellationToken.None);

        Assert.Null(result);
    }

    // Exactly at the cap is allowed; one more byte is rejected.
    [Fact]
    public async Task ReadCappedAsync_boundary_exact_ok_over_by_one_rejected()
    {
        var data = new byte[] { 1, 2, 3, 4 };

        using var exact = new MemoryStream(data);
        Assert.NotNull(await LineContentService.ReadCappedAsync(exact, maxBytes: 4, CancellationToken.None));

        using var over = new MemoryStream(data);
        Assert.Null(await LineContentService.ReadCappedAsync(over, maxBytes: 3, CancellationToken.None));
    }

    [Fact]
    public async Task ReadCappedAsync_empty_stream_returns_empty()
    {
        using var stream = new MemoryStream([]);

        var result = await LineContentService.ReadCappedAsync(stream, maxBytes: 10, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
