using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;

namespace LineHfBot.Line;

/// <summary>Sends messages to LINE users. Reply is free (short-lived token); push is the fallback.</summary>
public interface ILineMessenger
{
    /// <summary>Send a text reply using the reply token. Returns false if it fails (e.g. token expired).</summary>
    Task<bool> TryReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken);

    /// <summary>Push a text message to a user.</summary>
    Task PushTextAsync(string userId, string text, CancellationToken cancellationToken);

    /// <summary>Push an image message (both URLs must be public HTTPS).</summary>
    Task PushImageAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken);

    /// <summary>Push a video message (mp4 + preview image, both public HTTPS).</summary>
    Task PushVideoAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken);
}

/// <summary>Thin wrapper over <see cref="MessagingClient"/>.</summary>
public sealed class LineMessenger(MessagingClient client, ILogger<LineMessenger> logger) : ILineMessenger
{
    public async Task<bool> TryReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken)
    {
        try
        {
            await client.Api.V2.Bot.Message.Reply.PostAsync(new ReplyMessageRequest
            {
                ReplyToken = replyToken,
                Messages = [new TextMessage { Type = "text", Text = text }],
            }, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Reply failed ({Detail}); will fall back to push.", DescribeLineError(ex));
            return false;
        }
    }

    public async Task PushTextAsync(string userId, string text, CancellationToken cancellationToken)
    {
        try
        {
            await client.Api.V2.Bot.Message.Push.PostAsync(new PushMessageRequest
            {
                To = userId,
                Messages = [new TextMessage { Type = "text", Text = text }],
            }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Push failed ({Detail}).", DescribeLineError(ex));
            throw;
        }
    }

    public async Task PushImageAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken)
    {
        try
        {
            await client.Api.V2.Bot.Message.Push.PostAsync(new PushMessageRequest
            {
                To = userId,
                Messages = [new ImageMessage
                {
                    Type = "image",
                    OriginalContentUrl = originalContentUrl,
                    PreviewImageUrl = previewImageUrl,
                }],
            }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Push image failed ({Detail}).", DescribeLineError(ex));
            throw;
        }
    }

    public async Task PushVideoAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken)
    {
        try
        {
            await client.Api.V2.Bot.Message.Push.PostAsync(new PushMessageRequest
            {
                To = userId,
                Messages = [new VideoMessage
                {
                    Type = "video",
                    OriginalContentUrl = originalContentUrl,
                    PreviewImageUrl = previewImageUrl,
                }],
            }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Push video failed ({Detail}).", DescribeLineError(ex));
            throw;
        }
    }

    // Extract LINE's validation details (message + per-property errors) from the Kiota error object.
    // Uses reflection so it works regardless of the exact generated error-type shape.
    private static string DescribeLineError(Exception ex)
    {
        var parts = new List<string> { $"{ex.GetType().Name}: {ex.Message}" };
        if (ex.GetType().GetProperty("Details")?.GetValue(ex) is System.Collections.IEnumerable details)
        {
            foreach (var d in details)
            {
                if (d is null) continue;
                var dt = d.GetType();
                var property = dt.GetProperty("Property")?.GetValue(d);
                var message = dt.GetProperty("Message")?.GetValue(d);
                parts.Add($"[{property}] {message}");
            }
        }
        return string.Join(" | ", parts);
    }
}
