using LineHfBot.Ai;
using LineHfBot.Chat;
using LineHfBot.Configuration;
using LineHfBot.Line;
using LineHfBot.Media;
using LineHfBot.State;
using LineHfBot.Text;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Microsoft.Extensions.Options;

namespace LineHfBot.Queue;

/// <summary>
/// Handles a dequeued item: chat via HF, image and video generation, plus /reset and /help.
/// Replies (free) when possible, falling back to push.
/// </summary>
public sealed class WorkProcessor(
    IChatService chat,
    IImageService imageService,
    IImageEditService imageEditService,
    IVideoService videoService,
    ILineContentService lineContent,
    ChatHistoryStore history,
    UserStateStore userState,
    MediaStore mediaStore,
    ProcessedEventStore processedEvents,
    ILineMessenger messenger,
    QuickReplyFactory quickReplies,
    UserMessages messages,
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
                    userState.Reset(item.UserId);
                    await SendAsync(item, messages.ResetDone, cancellationToken);
                    break;
                case WorkKind.Help:
                    await SendAsync(item, messages.Help, cancellationToken);
                    break;
                case WorkKind.Chat:
                    await SendAsync(item, await chat.CompleteAsync(item.UserId, item.Text, cancellationToken), cancellationToken);
                    break;
                case WorkKind.Image:
                    await HandleImageAsync(item, cancellationToken);
                    break;
                case WorkKind.ImageEdit:
                    await HandleImageEditAsync(item, cancellationToken);
                    break;
                case WorkKind.ReceiveImage:
                    await HandleReceiveImageAsync(item, cancellationToken);
                    break;
                case WorkKind.Video:
                    if (appOptions.Value.VideoEnabled)
                    {
                        await HandleVideoAsync(item, cancellationToken);
                    }
                    else
                    {
                        // text-to-video needs a provider integration; ships disabled for now.
                        await SendAsync(item, messages.NotYetImplemented, cancellationToken);
                    }
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to handle item kind={Kind} user={User}", item.Kind, item.UserId);
            // Best-effort notification; ignore secondary failures.
            try { await SendAsync(item, messages.Error, cancellationToken); } catch { /* ignore */ }
        }
    }

    private async Task HandleImageAsync(WorkItem item, CancellationToken cancellationToken)
    {
        var baseUrl = await PrepareMediaAsync(item, messages.ImageUsage, messages.GeneratingImage, cancellationToken);
        if (baseUrl is null)
        {
            return;
        }

        var media = await imageService.GenerateAsync(item.Text, cancellationToken);
        var id = mediaStore.Save(media);
        // Remember this generation so the user can regenerate (same prompt) or edit (3b).
        userState.SetLastImage(item.UserId, item.Text, id);
        var url = $"{baseUrl}/media/{id}";
        await messenger.PushImageAsync(item.UserId, url, url, cancellationToken, quickReplies.ImageResult);
    }

    private async Task HandleImageEditAsync(WorkItem item, CancellationToken cancellationToken)
    {
        var baseUrl = await PrepareMediaAsync(item, messages.EditPrompt, messages.EditingImage, cancellationToken);
        if (baseUrl is null)
        {
            return;
        }

        // The reference image is the user's last generation; it lives in the TTL media cache and may have expired.
        if (string.IsNullOrEmpty(item.RefImageId) ||
            !mediaStore.TryGet(item.RefImageId, out var reference) || reference is null)
        {
            await SendAsync(item, messages.EditImageExpired, cancellationToken);
            return;
        }

        var media = await imageEditService.GenerateAsync(reference.Bytes, item.Text, cancellationToken);
        var id = mediaStore.Save(media);
        // Chain further edits on the edited result; keep LastPrompt so regenerate still uses the original prompt.
        userState.SetLastImageId(item.UserId, id);
        var url = $"{baseUrl}/media/{id}";
        await messenger.PushImageAsync(item.UserId, url, url, cancellationToken, quickReplies.ImageResult);
    }

    /// <summary>
    /// A user sent a photo (item.Text = LINE messageId): download it, store it in the media cache, and
    /// make it the working image for editing (LastImageId + AwaitingEdit, atomically), then ask how to
    /// edit it. The next plain text is handled by the existing edit flow. No PublicBaseUrl is needed
    /// here — the image is only re-served when an edited result is produced.
    /// </summary>
    private async Task HandleReceiveImageAsync(WorkItem item, CancellationToken cancellationToken)
    {
        // Idempotency: LINE may redeliver webhooks; do not download/store the same image twice.
        if (!processedEvents.TryMarkNew(item.WebhookEventId))
        {
            logger.LogInformation("Duplicate ReceiveImage event skipped: eventId={EventId}", item.WebhookEventId);
            return;
        }

        GeneratedMedia media;
        try
        {
            media = await lineContent.FetchImageAsync(item.Text, cancellationToken);
        }
        catch (ImageTooLargeException)
        {
            await SendAsync(item, messages.ImageTooLarge, cancellationToken);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to fetch user image: user={User} messageId={MessageId}", item.UserId, item.Text);
            await SendAsync(item, messages.ImageReceiveFailed, cancellationToken);
            return;
        }

        var id = mediaStore.Save(media);
        userState.SetReceivedImage(item.UserId, id);
        await SendAsync(item, messages.ImageReceived, cancellationToken);
    }

    private async Task HandleVideoAsync(WorkItem item, CancellationToken cancellationToken)
    {
        var baseUrl = await PrepareMediaAsync(item, messages.VideoUsage, messages.GeneratingVideo, cancellationToken);
        if (baseUrl is null)
        {
            return;
        }

        var media = await videoService.GenerateAsync(item.Text, cancellationToken);
        var url = $"{baseUrl}/media/{mediaStore.Save(media)}";
        await messenger.PushVideoAsync(item.UserId, url, $"{baseUrl}{VideoPreview.Path}", cancellationToken, quickReplies.VideoResult);
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
            await SendAsync(item, messages.Error, cancellationToken);
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
    private async Task SendAsync(WorkItem item, string text, CancellationToken cancellationToken, QuickReply? quickReply = null)
    {
        if (!string.IsNullOrEmpty(item.ReplyToken) &&
            await messenger.TryReplyTextAsync(item.ReplyToken, text, cancellationToken, quickReply))
        {
            return;
        }

        if (!string.IsNullOrEmpty(item.UserId))
        {
            await messenger.PushTextAsync(item.UserId, text, cancellationToken, quickReply);
        }
    }
}
