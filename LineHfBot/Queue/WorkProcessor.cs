using LineHfBot.Ai;
using LineHfBot.Chat;
using LineHfBot.Configuration;
using LineHfBot.Line;
using LineHfBot.Media;
using LineHfBot.Text;
using Microsoft.Extensions.Options;

namespace LineHfBot.Queue;

/// <summary>
/// Handles a dequeued item: chat via HF, image and video generation, plus /reset and /help.
/// Replies (free) when possible, falling back to push.
/// </summary>
public sealed class WorkProcessor(
    IChatService chat,
    IImageService imageService,
    IVideoService videoService,
    ChatHistoryStore history,
    MediaStore mediaStore,
    ProcessedEventStore processedEvents,
    ILineMessenger messenger,
    IOptions<AppOptions> appOptions,
    ILogger<WorkProcessor> logger) : IWorkProcessor
{
    public async Task ProcessAsync(WorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            switch (item.Kind)
            {
                case WorkKind.Reset:
                    history.Reset(item.UserId);
                    await SendAsync(item, UserMessages.ResetDone, cancellationToken);
                    break;
                case WorkKind.Help:
                    await SendAsync(item, UserMessages.Help, cancellationToken);
                    break;
                case WorkKind.Chat:
                    await SendAsync(item, await chat.CompleteAsync(item.UserId, item.Text, cancellationToken), cancellationToken);
                    break;
                case WorkKind.Image:
                    await HandleImageAsync(item, cancellationToken);
                    break;
                case WorkKind.Video:
                    if (appOptions.Value.VideoEnabled)
                    {
                        await HandleVideoAsync(item, cancellationToken);
                    }
                    else
                    {
                        // text-to-video needs a provider integration; ships disabled for now.
                        await SendAsync(item, UserMessages.NotYetImplemented, cancellationToken);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle item kind={Kind} user={User}", item.Kind, item.UserId);
            // Best-effort notification; ignore secondary failures.
            try { await SendAsync(item, UserMessages.Error, cancellationToken); } catch { /* ignore */ }
        }
    }

    private async Task HandleImageAsync(WorkItem item, CancellationToken cancellationToken)
    {
        var baseUrl = await PrepareMediaAsync(item, UserMessages.ImageUsage, UserMessages.GeneratingImage, cancellationToken);
        if (baseUrl is null)
        {
            return;
        }

        var media = await imageService.GenerateAsync(item.Text, cancellationToken);
        var url = $"{baseUrl}/media/{mediaStore.Save(media)}";
        await messenger.PushImageAsync(item.UserId, url, url, cancellationToken);
    }

    private async Task HandleVideoAsync(WorkItem item, CancellationToken cancellationToken)
    {
        var baseUrl = await PrepareMediaAsync(item, UserMessages.VideoUsage, UserMessages.GeneratingVideo, cancellationToken);
        if (baseUrl is null)
        {
            return;
        }

        var media = await videoService.GenerateAsync(item.Text, cancellationToken);
        var url = $"{baseUrl}/media/{mediaStore.Save(media)}";
        await messenger.PushVideoAsync(item.UserId, url, $"{baseUrl}{VideoPreview.Path}", cancellationToken);
    }

    /// <summary>
    /// Shared prelude for media generation: dedupe redelivered events, validate the prompt and
    /// PublicBaseUrl, and send the "generating" ack. Returns the trimmed base URL to proceed, or
    /// null when the caller should stop (a response has already been sent).
    /// </summary>
    private async Task<string?> PrepareMediaAsync(WorkItem item, string usage, string generating, CancellationToken cancellationToken)
    {
        // Idempotency: LINE may redeliver webhooks; do not generate the same media twice.
        if (!processedEvents.TryMarkNew(item.WebhookEventId))
        {
            logger.LogInformation("Duplicate {Kind} event skipped: eventId={EventId}", item.Kind, item.WebhookEventId);
            return null;
        }

        if (string.IsNullOrWhiteSpace(item.Text))
        {
            await SendAsync(item, usage, cancellationToken);
            return null;
        }

        var baseUrl = appOptions.Value.PublicBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogError("App:PublicBaseUrl is not set; cannot deliver generated media.");
            await SendAsync(item, UserMessages.Error, cancellationToken);
            return null;
        }

        // Immediate ack via the (free) reply token; the result is pushed when ready.
        if (!string.IsNullOrEmpty(item.ReplyToken))
        {
            await messenger.TryReplyTextAsync(item.ReplyToken, generating, cancellationToken);
        }

        return baseUrl.TrimEnd('/');
    }

    /// <summary>Reply (free) if possible, otherwise push.</summary>
    private async Task SendAsync(WorkItem item, string text, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(item.ReplyToken) &&
            await messenger.TryReplyTextAsync(item.ReplyToken, text, cancellationToken))
        {
            return;
        }

        if (!string.IsNullOrEmpty(item.UserId))
        {
            await messenger.PushTextAsync(item.UserId, text, cancellationToken);
        }
    }
}
