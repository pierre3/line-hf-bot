using LineHfBot.Configuration;
using LineHfBot.Media;
using Line.OpenApi.Messaging;
using Microsoft.Extensions.Options;

namespace LineHfBot.Line;

/// <summary>Raised when a user-sent image exceeds the configured download cap.</summary>
public sealed class ImageTooLargeException(long maxBytes) : Exception($"Image exceeds the {maxBytes}-byte limit.")
{
    public long MaxBytes { get; } = maxBytes;
}

/// <summary>Downloads user-sent media (images) from the LINE Content API.</summary>
public interface ILineContentService
{
    /// <summary>
    /// Fetch the bytes of a user-sent image by its LINE messageId. Throws
    /// <see cref="ImageTooLargeException"/> when the content exceeds the cap; other failures bubble up.
    /// </summary>
    Task<GeneratedMedia> FetchImageAsync(string messageId, CancellationToken cancellationToken);
}

/// <summary>
/// Fetches user-sent content via the LINE data-plane facade (<see cref="MessagingClient.Blob"/>,
/// host api-data.line.me). The blob client is not DI-registered on its own; we go through the
/// registered <see cref="MessagingClient"/> like <see cref="LineMessenger"/> does.
/// </summary>
public sealed class LineContentService(MessagingClient client, IOptions<LineOptions> options, ILogger<LineContentService> logger)
    : ILineContentService
{
    // Content type is not exposed by the generated binary endpoint; user images from LINE are JPEG.
    private const string DefaultContentType = "image/jpeg";

    public async Task<GeneratedMedia> FetchImageAsync(string messageId, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var timeout = TimeSpan.FromSeconds(Math.Max(5, opts.ContentFetchTimeoutSeconds));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        // GET /v2/bot/message/{messageId}/content on the blob (data) plane -> raw stream.
        await using var stream = await client.Blob.V2.Bot.Message[messageId].Content.GetAsync(
            cancellationToken: cts.Token);
        if (stream is null)
        {
            throw new InvalidOperationException($"LINE returned no content for messageId {messageId}.");
        }

        var bytes = await ReadCappedAsync(stream, opts.MaxIncomingImageBytes, cts.Token);
        if (bytes is null)
        {
            logger.LogWarning("User image exceeds cap ({Max} bytes); rejected.", opts.MaxIncomingImageBytes);
            throw new ImageTooLargeException(opts.MaxIncomingImageBytes);
        }

        return new GeneratedMedia(bytes, DefaultContentType);
    }

    /// <summary>
    /// Read a stream fully into memory, but stop and return null once more than <paramref name="maxBytes"/>
    /// have been read (so an oversized/unknown-length stream cannot exhaust memory).
    /// </summary>
    internal static async Task<byte[]?> ReadCappedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
