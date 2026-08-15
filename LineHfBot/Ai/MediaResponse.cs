using LineHfBot.Media;

namespace LineHfBot.Ai;

/// <summary>
/// Turns a successful HF generation response into media bytes, handling both provider styles:
/// raw media bytes, or a JSON body carrying a URL (re-fetched through the SSRF-guarded helper).
/// Shared by the text-to-image, text-to-video and image-to-image services.
/// </summary>
internal static class MediaResponse
{
    public static async Task<GeneratedMedia> ReadAsync(
        HttpClient http,
        HttpResponseMessage response,
        IReadOnlyCollection<string> allowedHosts,
        string defaultContentType,
        CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

        // Some providers return JSON with a URL to the result rather than the bytes directly.
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var mediaUrl = MediaUrlExtractor.TryExtract(json)
                ?? throw new InvalidOperationException("Media URL not found in provider response.");
            var (bytes, refetchedType) = await MediaRefetch.FetchAsync(http, mediaUrl, allowedHosts, cancellationToken);
            return new GeneratedMedia(bytes, string.IsNullOrEmpty(refetchedType) ? defaultContentType : refetchedType);
        }

        var raw = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new GeneratedMedia(raw, string.IsNullOrEmpty(contentType) ? defaultContentType : contentType);
    }
}
