namespace LineHfBot.Queue;

/// <summary>Kind of work, decided from the user's input (command or plain text).</summary>
public enum WorkKind
{
    Chat,
    Image,
    ImageEdit,
    ReceiveImage,
    Video,
    Reset,
    Help,
}

/// <summary>A single unit of background work, built from a webhook event.</summary>
/// <param name="Kind">Work kind.</param>
/// <param name="UserId">Sender's LINE user id.</param>
/// <param name="ReplyToken">Reply token for the immediate ack (short-lived, single use).</param>
/// <param name="Text">Prompt text (command prefix already stripped). For ImageEdit this is the edit instruction;
/// for ReceiveImage this is the LINE messageId of the image to download.</param>
/// <param name="WebhookEventId">Unique id used for idempotency.</param>
/// <param name="RefImageId">For ImageEdit: media id of the reference image to edit (from the last generation).</param>
public sealed record WorkItem(
    WorkKind Kind,
    string UserId,
    string ReplyToken,
    string Text,
    string WebhookEventId,
    string? RefImageId = null);
