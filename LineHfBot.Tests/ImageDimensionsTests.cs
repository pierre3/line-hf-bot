using System.Text;
using LineHfBot.Ai;

namespace LineHfBot.Tests;

/// <summary>Header-only PNG/JPEG dimension reader used to pick a supported image-to-video aspect ratio.</summary>
public class ImageDimensionsTests
{
    private static byte[] Png(int width, int height)
    {
        var b = new byte[24];
        b[0] = 0x89; b[1] = 0x50; b[2] = 0x4E; b[3] = 0x47; b[4] = 0x0D; b[5] = 0x0A; b[6] = 0x1A; b[7] = 0x0A;
        b[11] = 0x0D; // IHDR length = 13
        b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        b[16] = (byte)(width >> 24); b[17] = (byte)(width >> 16); b[18] = (byte)(width >> 8); b[19] = (byte)width;
        b[20] = (byte)(height >> 24); b[21] = (byte)(height >> 16); b[22] = (byte)(height >> 8); b[23] = (byte)height;
        return b;
    }

    // FFD8 (SOI) + FFC0 (SOF0) segment: length(2)=17, precision(1)=8, height(2), width(2), then component pad.
    private static byte[] Jpeg(int width, int height)
    {
        return
        [
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        ];
    }

    [Fact]
    public void Reads_png_dimensions()
    {
        Assert.Equal((816, 1104), ImageDimensions.TryGet(Png(816, 1104)));
        Assert.Equal((1920, 1080), ImageDimensions.TryGet(Png(1920, 1080)));
    }

    [Fact]
    public void Reads_jpeg_dimensions_including_after_a_leading_segment()
    {
        Assert.Equal((640, 480), ImageDimensions.TryGet(Jpeg(640, 480)));

        // A JPEG with an APP0 (JFIF) segment before the SOF0 — the scanner must skip it by length.
        byte[] withApp0 =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, // APP0, length 16
            0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x04, 0x38, 0x07, 0x80, // SOF0: h=1080, w=1920
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        ];
        Assert.Equal((1920, 1080), ImageDimensions.TryGet(withApp0));
    }

    [Fact]
    public void Returns_null_for_unrecognized_or_short_input()
    {
        Assert.Null(ImageDimensions.TryGet(Encoding.UTF8.GetBytes("not-an-image")));
        Assert.Null(ImageDimensions.TryGet([1, 2, 3]));
        Assert.Null(ImageDimensions.TryGet([]));
    }
}
