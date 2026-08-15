using LineHfBot.Configuration;
using LineHfBot.Line;
using LineHfBot.Messaging;
using LineHfBot.Queue;
using LineHfBot.State;
using LineHfBot.Text;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Line.OpenApi.Messaging.Webhook.Generated.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineHfBot.Tests;

public class MessageDispatcherTests
{
    private sealed class FakeQueue : IWorkQueue
    {
        public List<WorkItem> Items { get; } = [];
        public bool TryEnqueue(WorkItem item) { Items.Add(item); return true; }
        public IAsyncEnumerable<WorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMessenger : ILineMessenger
    {
        public List<string> Replies { get; } = [];
        public Task<bool> TryReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken, QuickReply? quickReply = null)
        { Replies.Add(text); return Task.FromResult(true); }
        public Task PushTextAsync(string userId, string text, CancellationToken cancellationToken, QuickReply? quickReply = null) => Task.CompletedTask;
        public Task PushImageAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken, QuickReply? quickReply = null) => Task.CompletedTask;
        public Task PushVideoAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken, QuickReply? quickReply = null) => Task.CompletedTask;
    }

    private static (MessageDispatcher Dispatcher, FakeQueue Queue, FakeMessenger Messenger, UserMessages Messages) Build()
    {
        var app = Options.Create(new AppOptions());
        var line = Options.Create(new LineOptions { ChannelAccessToken = "test-token" });
        var messages = new UserMessages(app);
        var queue = new FakeQueue();
        var messenger = new FakeMessenger();
        var richMenu = new RichMenuManager(line, app, NullLogger<RichMenuManager>.Instance);
        var dispatcher = new MessageDispatcher(
            queue, messenger, new UserStateStore(), richMenu, messages, NullLogger<MessageDispatcher>.Instance);
        return (dispatcher, queue, messenger, messages);
    }

    private static CallbackRequest ImageEvent(string messageId, ContentProvider_type? provider)
    {
        var img = new ImageMessageContent { Type = "image", Id = messageId };
        if (provider is not null)
        {
            img.ContentProvider = new ContentProvider { Type = provider };
        }
        return new CallbackRequest
        {
            Events =
            [
                new MessageEvent
                {
                    Type = "message",
                    ReplyToken = "rt",
                    WebhookEventId = "ev1",
                    Source = new UserSource { Type = "user", UserId = "u1" },
                    Message = img,
                },
            ],
        };
    }

    // A user-sent image (LINE-provided) is enqueued as ReceiveImage carrying the messageId; no reply yet.
    [Fact]
    public async Task Image_from_line_provider_enqueues_ReceiveImage()
    {
        var (dispatcher, queue, messenger, _) = Build();

        await dispatcher.DispatchAsync(ImageEvent("msg-123", ContentProvider_type.Line), CancellationToken.None);

        var item = Assert.Single(queue.Items);
        Assert.Equal(WorkKind.ReceiveImage, item.Kind);
        Assert.Equal("msg-123", item.Text);
        Assert.Equal("u1", item.UserId);
        Assert.Empty(messenger.Replies);
    }

    // A null content provider is treated as LINE-provided (fetchable).
    [Fact]
    public async Task Image_with_no_provider_enqueues_ReceiveImage()
    {
        var (dispatcher, queue, _, _) = Build();

        await dispatcher.DispatchAsync(ImageEvent("msg-9", provider: null), CancellationToken.None);

        Assert.Equal(WorkKind.ReceiveImage, Assert.Single(queue.Items).Kind);
    }

    // External-provider images are declined (no enqueue, no arbitrary URL fetch).
    [Fact]
    public async Task Image_from_external_provider_is_declined()
    {
        var (dispatcher, queue, messenger, messages) = Build();

        await dispatcher.DispatchAsync(ImageEvent("msg-x", ContentProvider_type.External), CancellationToken.None);

        Assert.Empty(queue.Items);
        Assert.Equal(messages.ImageSourceUnsupported, Assert.Single(messenger.Replies));
    }
}
