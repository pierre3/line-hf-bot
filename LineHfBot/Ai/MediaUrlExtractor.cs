using System.Text.Json;

namespace LineHfBot.Ai;

/// <summary>
/// Best-effort extraction of a media (image or video) URL from common provider JSON shapes.
/// Shared by <see cref="HuggingFaceImageService"/> and <see cref="HuggingFaceVideoService"/> so both
/// paths accept the union of provider schemas (fal-ai / replicate / nebius / hf-inference variants).
/// </summary>
internal static class MediaUrlExtractor
{
    /// <summary>
    /// Returns the first media URL found, trying (in order): url / output / image / video / images[0] / data[0].url.
    /// "video" is kept for backward compatibility with the previous video-only extractor.
    /// Returns null when no known shape matches.
    /// </summary>
    public static string? TryExtract(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Top-level string properties: url / output / image.
            foreach (var key in (ReadOnlySpan<string>)["url", "output", "image"])
            {
                if (TryGetString(root, key, out var s))
                {
                    return s;
                }
            }

            // "video": string or { url } (legacy video shape).
            if (root.TryGetProperty("video", out var video))
            {
                if (video.ValueKind == JsonValueKind.String)
                {
                    return video.GetString();
                }
                if (TryGetString(video, "url", out var vu))
                {
                    return vu;
                }
            }

            // "images": [ "https://..." ] or [ { "url": "https://..." } ].
            if (root.TryGetProperty("images", out var images) &&
                images.ValueKind == JsonValueKind.Array &&
                images.GetArrayLength() > 0)
            {
                var first = images[0];
                if (first.ValueKind == JsonValueKind.String)
                {
                    return first.GetString();
                }
                if (TryGetString(first, "url", out var iu))
                {
                    return iu;
                }
            }

            // "data": [ { "url": "https://..." } ].
            if (root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array &&
                data.GetArrayLength() > 0 &&
                TryGetString(data[0], "url", out var du))
            {
                return du;
            }

            return null;
        }
    }

    private static bool TryGetString(JsonElement obj, string name, out string? value)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(name, out var el) &&
            el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString();
            return !string.IsNullOrEmpty(value);
        }
        value = null;
        return false;
    }
}
