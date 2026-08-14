using LineHfBot.Ai;
using LineHfBot.Chat;
using LineHfBot.Configuration;
using LineHfBot.Line;
using LineHfBot.Media;
using LineHfBot.Text;
using Microsoft.Extensions.Options;

namespace LineHfBot.Queue;

/// <summary>
/// Handles a dequeued item: chat via HF, image generation, plus /reset and /help.
/// Video is a placeholder until its increment lands. Replies (free) when possible, falling back to push.
/// </summary>
public sealed class WorkProcessor(
    IChatService chat,
    IImageService imageService,
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
                    await SendAsync(item, UserMessages.NotYetImplemented, cancellationToken);
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
        // Idempotency: LINE may redeliver webhooks; do not generate the same image twice.
        if (!processedEvents.TryMarkNew(item.WebhookEventId))
        {
            logger.LogInformation("Duplicate image event skipped: eventId={EventId}", item.WebhookEventId);
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Text))
        {
            await SendAsync(item, UserMessages.ImageUsage, cancellationToken);
            return;
        }

        var baseUrl = appOptions.Value.PublicBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogError("App:PublicBaseUrl is not set; cannot deliver generated media.");
            await SendAsync(item, UserMessages.Error, cancellationToken);
            return;
        }

        // Immediate ack via the (free) reply token; the result is pushed when ready.
        if (!string.IsNullOrEmpty(item.ReplyToken))
        {
            await messenger.TryReplyTextAsync(item.ReplyToken, UserMessages.GeneratingImage, cancellationToken);
        }

        var media = await imageService.GenerateAsync(item.Text, cancellationToken);
        var id = mediaStore.Save(media);
        var url = $"{baseUrl.TrimEnd('/')}/media/{id}";

        await messenger.PushImageAsync(item.UserId, url, url, cancellationToken);
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
