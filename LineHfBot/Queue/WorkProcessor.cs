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
    IImageToVideoService imageToVideoService,
    IVisionService visionService,
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
                case WorkKind.Vision:
                    await HandleVisionAsync(item, cancellationToken);
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
                case WorkKind.ImageToVideo:
                    if (appOptions.Value.VideoEnabled)
                    {
                        await HandleImageToVideoAsync(item, cancellationToken);
                    }
                    else
                    {
                        // image-to-video runs on the same credit-heavy fal provider as /video; same opt-in gate.
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
    /// make it the working image. With vision enabled we leave the next action open (Pending=None) and
    /// offer Edit/Ask via quick reply; with vision disabled we arm the edit flow (Pending=Edit) and ask
    /// how to edit it (spec04 behavior). No PublicBaseUrl is needed here — the image is only re-served
    /// when an edited result is produced.
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
        if (appOptions.Value.VisionEnabled)
        {
            // Let the user choose: edit the photo, or ask a question about it.
            userState.SetReceivedImage(item.UserId, id, PendingAction.None);
            await SendAsync(item, messages.ImageReceivedChoose, cancellationToken, quickReplies.ReceivedImageChoices);
        }
        else
        {
            // Vision off: keep spec04 behavior — the next plain text edits the image.
            userState.SetReceivedImage(item.UserId, id, PendingAction.Edit);
            await SendAsync(item, messages.ImageReceived, cancellationToken);
        }
    }

    /// <summary>
    /// Answer a question about the user's working image (vision/VQA), continuing a conversational session when
    /// one is open for this image (spec09). The reference image lives in the TTL media cache and may have expired
    /// (expiry ends the session). Vision can be slow, so we ack via the (free) reply token and push the answer.
    /// The service returns a display-ready string (answer / timeout / empty). A successful turn is appended to the
    /// session (bounded by <see cref="AppOptions.VisionMaxTurns"/>) and, on the first successful turn, the answer
    /// carries a follow-up hint; a timeout/empty turn is not accumulated and does not open a session. Only non-2xx
    /// throws and is surfaced as the generic error by the top-level handler. No PublicBaseUrl needed.
    /// </summary>
    private async Task HandleVisionAsync(WorkItem item, CancellationToken cancellationToken)
    {
        // Idempotency: LINE may redeliver the question text event.
        if (!processedEvents.TryMarkNew(item.WebhookEventId))
        {
            logger.LogInformation("Duplicate Vision event skipped: eventId={EventId}", item.WebhookEventId);
            return;
        }

        if (string.IsNullOrEmpty(item.RefImageId) ||
            !mediaStore.TryGet(item.RefImageId, out var reference) || reference is null)
        {
            // The image (and thus the session) is gone; end the session so plain text stops following up.
            userState.ClearVisionSession(item.UserId);
            await SendAsync(item, messages.VisionImageExpired, cancellationToken);
            return;
        }

        var history = userState.GetVisionHistory(item.UserId, item.RefImageId);
        var firstTurn = history.Count == 0;

        // Immediate ack via the (free) reply token; the answer is pushed when ready.
        if (!string.IsNullOrEmpty(item.ReplyToken))
        {
            await messenger.TryReplyTextAsync(item.ReplyToken, messages.VisionThinking, cancellationToken);
        }

        var answer = await visionService.AnswerAsync(reference.Bytes, reference.ContentType, history, item.Text, cancellationToken);

        // A timeout/empty answer is a non-answer (same display strings as the service contract): do not add it to
        // the context and do not open a session (a first failed turn falls back to spec07 one-shot behavior).
        var succeeded = !string.Equals(answer, messages.Timeout, StringComparison.Ordinal) &&
                        !string.Equals(answer, messages.EmptyAnswer, StringComparison.Ordinal);
        if (succeeded)
        {
            userState.AppendVisionTurn(item.UserId, item.RefImageId, new VisionTurn(item.Text, answer), appOptions.Value.VisionMaxTurns);
        }

        // Hint only on the first successful turn (the session just opened); the VisionAnswer quick reply
        // (edit / animate / chat) is always offered so the exit path is available on every answer.
        var text = succeeded && firstTurn ? $"{answer}\n{messages.VisionFollowupHint}" : answer;
        await messenger.PushTextAsync(item.UserId, text, cancellationToken, quickReplies.VisionAnswer);
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
    /// Animate the user's working image (image-to-video): the reference image is the last generation/edit or a
    /// received photo, and item.Text is the motion instruction. Mirrors <see cref="HandleImageEditAsync"/>
    /// (reference lookup) but pushes a video like <see cref="HandleVideoAsync"/>. Timeout/no-notify on OCE
    /// matches text-to-video and image-edit (see the top-level catch).
    /// </summary>
    private async Task HandleImageToVideoAsync(WorkItem item, CancellationToken cancellationToken)
    {
        var baseUrl = await PrepareMediaAsync(item, messages.AnimatePrompt, messages.GeneratingVideo, cancellationToken);
        if (baseUrl is null)
        {
            return;
        }

        // The reference image lives in the TTL media cache and may have expired.
        if (string.IsNullOrEmpty(item.RefImageId) ||
            !mediaStore.TryGet(item.RefImageId, out var reference) || reference is null)
        {
            await SendAsync(item, messages.EditImageExpired, cancellationToken);
            return;
        }

        var media = await imageToVideoService.GenerateAsync(reference.Bytes, reference.ContentType, item.Text, cancellationToken);
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
