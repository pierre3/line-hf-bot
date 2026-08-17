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

public class WorkProcessorVisionTests
{
    private sealed class FakeMessenger : ILineMessenger
    {
        public List<string> Replies { get; } = [];
        public List<string> Pushes { get; } = [];
        public Task<bool> TryReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken, QuickReply? quickReply = null)
        { Replies.Add(text); return Task.FromResult(true); }
        public Task PushTextAsync(string userId, string text, CancellationToken cancellationToken, QuickReply? quickReply = null)
        { Pushes.Add(text); return Task.CompletedTask; }
        public Task PushImageAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken, QuickReply? quickReply = null) => Task.CompletedTask;
        public Task PushVideoAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken, QuickReply? quickReply = null) => Task.CompletedTask;
    }

    private sealed class FakeVision(string answer) : IVisionService
    {
        public int Calls { get; private set; }
        public byte[]? LastImage { get; private set; }
        public string? LastQuestion { get; private set; }
        public Task<string> AnswerAsync(byte[] image, string mediaType, string question, CancellationToken ct)
        {
            Calls++;
            LastImage = image;
            LastQuestion = question;
            return Task.FromResult(answer);
        }
    }

    private sealed class UnusedImage : IImageService
    { public Task<GeneratedMedia> GenerateAsync(string prompt, CancellationToken ct) => throw new NotSupportedException(); }
    private sealed class UnusedVideo : IVideoService
    { public Task<GeneratedMedia> GenerateAsync(string prompt, CancellationToken ct) => throw new NotSupportedException(); }
    private sealed class UnusedImageToVideo : IImageToVideoService
    { public Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string referenceContentType, string prompt, CancellationToken ct) => throw new NotSupportedException(); }
    private sealed class UnusedEdit : IImageEditService
    { public Task<GeneratedMedia> GenerateAsync(byte[] referenceImage, string instruction, CancellationToken ct) => throw new NotSupportedException(); }
    private sealed class UnusedChat : IChatService
    { public Task<string> CompleteAsync(string userId, string userText, CancellationToken ct) => throw new NotSupportedException(); }
    private sealed class UnusedContent : ILineContentService
    { public Task<GeneratedMedia> FetchImageAsync(string messageId, CancellationToken ct) => throw new NotSupportedException(); }

    private static (WorkProcessor Proc, FakeMessenger Msg, FakeVision Vision, MediaStore Media, UserMessages Messages)
        Build(string answer)
    {
        var app = Options.Create(new AppOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var messages = new UserMessages(app);
        var media = new MediaStore(cache, app);
        var msg = new FakeMessenger();
        var vision = new FakeVision(answer);
        var proc = new WorkProcessor(
            new UnusedChat(), new UnusedImage(), new UnusedEdit(), new UnusedVideo(), new UnusedImageToVideo(), vision, new UnusedContent(),
            new ChatHistoryStore(Options.Create(new ChatOptions())), new UserStateStore(), media,
            new ProcessedEventStore(cache), msg, new QuickReplyFactory(messages, app), messages,
            app, NullLogger<WorkProcessor>.Instance);
        return (proc, msg, vision, media, messages);
    }

    private static WorkItem Item(string refImageId, string eventId = "ev-v") =>
        new(WorkKind.Vision, "u1", "rt", "what is written here?", eventId, refImageId);

    [Fact]
    public async Task Success_acks_then_pushes_answer()
    {
        var (proc, msg, vision, media, messages) = Build("It says HELLO.");
        var id = media.Save(new GeneratedMedia([1, 2, 3], "image/jpeg"));

        await proc.ProcessAsync(Item(id), CancellationToken.None);

        Assert.Equal(1, vision.Calls);
        Assert.Equal([1, 2, 3], vision.LastImage);
        Assert.Equal("what is written here?", vision.LastQuestion);
        Assert.Equal(messages.VisionThinking, Assert.Single(msg.Replies)); // ack via reply token
        Assert.Equal("It says HELLO.", Assert.Single(msg.Pushes));          // answer pushed
    }

    [Fact]
    public async Task Expired_reference_image_notifies_and_does_not_call_vision()
    {
        var (proc, msg, vision, _, messages) = Build("unused");

        await proc.ProcessAsync(Item("missing-id"), CancellationToken.None);

        Assert.Equal(0, vision.Calls);
        Assert.Equal(messages.VisionImageExpired, Assert.Single(msg.Replies));
        Assert.Empty(msg.Pushes);
    }

    // LINE may redeliver the question event; the same event id must not trigger a second vision call.
    [Fact]
    public async Task Duplicate_event_is_skipped()
    {
        var (proc, msg, vision, media, _) = Build("answer");
        var id = media.Save(new GeneratedMedia([9], "image/png"));

        await proc.ProcessAsync(Item(id, "dup-1"), CancellationToken.None);
        await proc.ProcessAsync(Item(id, "dup-1"), CancellationToken.None);

        Assert.Equal(1, vision.Calls);
        Assert.Single(msg.Pushes);
    }
}
