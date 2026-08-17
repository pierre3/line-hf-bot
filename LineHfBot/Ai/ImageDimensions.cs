namespace LineHfBot.Ai;

/// <summary>
/// Minimal image dimension reader for PNG and JPEG (the formats we hand to providers: generated PNGs and
/// LINE/JPEG photos). Reads only the header — no decoding, no external dependency (the runtime image is
/// chiseled/distroless, so System.Drawing/GDI is unavailable). Returns null for anything it doesn't
/// recognize; callers fall back to a safe default.
/// </summary>
internal static class ImageDimensions
{
    public static (int Width, int Height)? TryGet(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 4)
        {
            return null;
        }

        // PNG: 8-byte signature, then the IHDR chunk with width @16 and height @20 (both big-endian uint32).
        if (bytes.Length >= 24 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            var w = ReadBe32(bytes, 16);
            var h = ReadBe32(bytes, 20);
            return w > 0 && h > 0 ? (w, h) : null;
        }

        // JPEG: SOI (FFD8), then scan segments for a Start-Of-Frame marker carrying height/width.
        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return ReadJpeg(bytes);
        }

        return null;
    }

    private static (int Width, int Height)? ReadJpeg(byte[] bytes)
    {
        var i = 2;
        while (i + 1 < bytes.Length)
        {
            // Skip fill bytes until a marker prefix (0xFF followed by a non-0xFF, non-0x00 byte).
            if (bytes[i] != 0xFF)
            {
                i++;
                continue;
            }
            var marker = bytes[i + 1];
            if (marker == 0xFF || marker == 0x00)
            {
                i++;
                continue;
            }
            // Standalone markers (SOI/EOI/RSTn) carry no length payload.
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
            {
                i += 2;
                continue;
            }
            // Start-Of-Frame markers carry the dimensions (exclude DHT/JPG/DAC = C4/C8/CC).
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                if (i + 8 >= bytes.Length)
                {
                    return null;
                }
                var h = (bytes[i + 5] << 8) | bytes[i + 6];
                var w = (bytes[i + 7] << 8) | bytes[i + 8];
                return w > 0 && h > 0 ? (w, h) : null;
            }
            // Otherwise skip this segment using its 2-byte big-endian length (includes the length bytes).
            if (i + 3 >= bytes.Length)
            {
                return null;
            }
            var segLen = (bytes[i + 2] << 8) | bytes[i + 3];
            if (segLen < 2)
            {
                return null;
            }
            i += 2 + segLen;
        }
        return null;
    }

    private static int ReadBe32(byte[] b, int offset) =>
        (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
}
