namespace LineHfBot.Media;

/// <summary>
/// Placeholder image used as the required previewImageUrl for LINE video messages.
/// TODO: replace with a real thumbnail (e.g. an extracted first frame) in a future increment.
/// </summary>
public static class VideoPreview
{
    public const string Path = "/assets/video-preview.png";
    public const string ContentType = "image/png";

    // Minimal 1x1 PNG placeholder.
    public static readonly byte[] Bytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
