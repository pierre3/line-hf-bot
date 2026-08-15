using LineHfBot.Ai;
using LineHfBot.Chat;
using LineHfBot.Configuration;
using LineHfBot.Line;
using LineHfBot.Media;
using LineHfBot.Queue;
using LineHfBot.State;
using LineHfBot.Text;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineHfBot.Tests;

public class WorkProcessorReceiveImageTests
{
    private sealed class FakeContent(Func<GeneratedMedia> responder) : ILineContentService
    {
        public Task<GeneratedMedia> FetchImageAsync(string messageId, CancellationToken cancellationToken) =>
            Task.FromResult(responder());
    }

    private sealed class FakeMessenger : ILineMessenger
    {
        public List<string> Replies { get; } = [];
        public Task<bool> TryReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken, QuickReply? quickReply = null)
        { Replies.Add(text); return Task.FromResult(true); }
        public Task PushTextAsync(string userId, string text, CancellationToken cancellationToken, QuickReply? quickReply = null)
        { Replies.Add(text); return Task.CompletedTask; }
        public Task PushImageAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken, QuickReply? quickReply = null) => Task.CompletedTask;
        public Task PushVideoAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken, QuickReply? quickReply = null) => Task.CompletedTask;
    }

    private sealed class UnusedImage : IImageService
    { public Task<GeneratedMedia> GenerateAsync(string prompt, CancellationToken ct) => throw new NotSupportedException(); }
    private sealed class UnusedVideo : IVideoService
    { public Task<GeneratedMedia> GenerateAsync(string prompt, CancellationToken ct) => throw new NotSupportedException(); }
    private sealed class UnusedEdit : IImageEditService
    { public Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string instruction, CancellationToken ct) => throw new NotSupportedException(); }
    private sealed class UnusedChat : IChatService
    { public Task<string> CompleteAsync(string userId, string userText, CancellationToken ct) => throw new NotSupportedException(); }

    private static (WorkProcessor Proc, FakeMessenger Msg, UserStateStore State, MediaStore Media, UserMessages Messages)
        Build(ILineContentService content)
    {
        var app = Options.Create(new AppOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var messages = new UserMessages(app);
        var state = new UserStateStore();
        var media = new MediaStore(cache, app);
        var msg = new FakeMessenger();
        var proc = new WorkProcessor(
            new UnusedChat(), new UnusedImage(), new UnusedEdit(), new UnusedVideo(), content,
            new ChatHistoryStore(Options.Create(new ChatOptions())), state, media,
            new ProcessedEventStore(cache), msg, new QuickReplyFactory(messages), messages,
            app, NullLogger<WorkProcessor>.Instance);
        return (proc, msg, state, media, messages);
    }

    private static WorkItem Item() => new(WorkKind.ReceiveImage, "u1", "rt", "msg-1", "ev-1");

    [Fact]
    public async Task Success_stores_image_arms_edit_and_prompts()
    {
        var (proc, msg, state, media, messages) = Build(
            new FakeContent(() => new GeneratedMedia([1, 2, 3], "image/jpeg")));

        await proc.ProcessAsync(Item(), CancellationToken.None);

        var s = state.Get("u1");
        Assert.NotNull(s.LastImageId);
        Assert.True(s.AwaitingEdit);
        Assert.Null(s.LastPrompt);
        Assert.True(media.TryGet(s.LastImageId!, out _));
        Assert.Equal(messages.ImageReceived, Assert.Single(msg.Replies));
    }

    [Fact]
    public async Task TooLarge_notifies_and_leaves_state_unchanged()
    {
        var (proc, msg, state, _, messages) = Build(
            new FakeContent(() => throw new ImageTooLargeException(100)));

        await proc.ProcessAsync(Item(), CancellationToken.None);

        Assert.Equal(messages.ImageTooLarge, Assert.Single(msg.Replies));
        var s = state.Get("u1");
        Assert.False(s.AwaitingEdit);
        Assert.Null(s.LastImageId);
    }

    // A fetch timeout is surfaced as TimeoutException (non-OCE) so the user is told (AC#7).
    [Fact]
    public async Task Timeout_notifies_receive_failed()
    {
        var (proc, msg, state, _, messages) = Build(
            new FakeContent(() => throw new TimeoutException("timed out")));

        await proc.ProcessAsync(Item(), CancellationToken.None);

        Assert.Equal(messages.ImageReceiveFailed, Assert.Single(msg.Replies));
        Assert.False(state.Get("u1").AwaitingEdit);
    }

    [Fact]
    public async Task Generic_fetch_failure_notifies_receive_failed()
    {
        var (proc, msg, _, _, messages) = Build(
            new FakeContent(() => throw new InvalidOperationException("boom")));

        await proc.ProcessAsync(Item(), CancellationToken.None);

        Assert.Equal(messages.ImageReceiveFailed, Assert.Single(msg.Replies));
    }
}
