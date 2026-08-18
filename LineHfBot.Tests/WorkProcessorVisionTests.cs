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
        public List<QuickReply?> PushQuickReplies { get; } = [];
        public Task<bool> TryReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken, QuickReply? quickReply = null)
        { Replies.Add(text); return Task.FromResult(true); }
        public Task PushTextAsync(string userId, string text, CancellationToken cancellationToken, QuickReply? quickReply = null)
        { Pushes.Add(text); PushQuickReplies.Add(quickReply); return Task.CompletedTask; }
        public Task PushImageAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken, QuickReply? quickReply = null) => Task.CompletedTask;
        public Task PushVideoAsync(string userId, string originalContentUrl, string previewImageUrl, CancellationToken cancellationToken, QuickReply? quickReply = null) => Task.CompletedTask;
    }

    private sealed class FakeVision(Func<IReadOnlyList<VisionTurn>, string> answer) : IVisionService
    {
        public FakeVision(string answer) : this(_ => answer) { }
        public int Calls { get; private set; }
        public byte[]? LastImage { get; private set; }
        public string? LastQuestion { get; private set; }
        public IReadOnlyList<VisionTurn> LastHistory { get; private set; } = [];
        public Task<string> AnswerAsync(byte[] image, string mediaType, IReadOnlyList<VisionTurn> history, string question, CancellationToken ct)
        {
            Calls++;
            LastImage = image;
            LastQuestion = question;
            LastHistory = history;
            return Task.FromResult(answer(history));
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

    private sealed record Harness(
        WorkProcessor Proc, FakeMessenger Msg, FakeVision Vision, MediaStore Media, UserMessages Messages, UserStateStore State);

    private static Harness Build(FakeVision vision, AppOptions? appOptions = null)
    {
        var app = Options.Create(appOptions ?? new AppOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var messages = new UserMessages(app);
        var media = new MediaStore(cache, app);
        var msg = new FakeMessenger();
        var state = new UserStateStore();
        var proc = new WorkProcessor(
            new UnusedChat(), new UnusedImage(), new UnusedEdit(), new UnusedVideo(), new UnusedImageToVideo(), vision, new UnusedContent(),
            new ChatHistoryStore(Options.Create(new ChatOptions())), state, media,
            new ProcessedEventStore(cache), msg, new QuickReplyFactory(messages, app), messages,
            app, NullLogger<WorkProcessor>.Instance);
        return new Harness(proc, msg, vision, media, messages, state);
    }

    private static Harness Build(string answer, AppOptions? appOptions = null) => Build(new FakeVision(answer), appOptions);

    private static WorkItem Item(string refImageId, string text = "what is written here?", string eventId = "ev-v") =>
        new(WorkKind.Vision, "u1", "rt", text, eventId, refImageId);

    [Fact]
    public async Task First_success_acks_pushes_answer_with_hint_and_quickreply()
    {
        var h = Build("It says HELLO.");
        var id = h.Media.Save(new GeneratedMedia([1, 2, 3], "image/jpeg"));

        await h.Proc.ProcessAsync(Item(id), CancellationToken.None);

        Assert.Equal(1, h.Vision.Calls);
        Assert.Equal([1, 2, 3], h.Vision.LastImage);
        Assert.Empty(h.Vision.LastHistory); // first turn: no prior context
        Assert.Equal("what is written here?", h.Vision.LastQuestion);
        Assert.Equal(h.Messages.VisionThinking, Assert.Single(h.Msg.Replies)); // ack via reply token
        // First successful turn: answer carries the follow-up hint.
        Assert.Equal($"It says HELLO.\n{h.Messages.VisionFollowupHint}", Assert.Single(h.Msg.Pushes));
        Assert.NotNull(Assert.Single(h.Msg.PushQuickReplies)); // VisionAnswer quick reply attached
        // Session opened against the image.
        Assert.True(h.State.Get("u1").VisionActive);
        Assert.Equal(id, h.State.Get("u1").VisionImageId);
    }

    // AC5/AC8: a follow-up turn passes the accumulated history, appends its own turn, and does NOT re-add the hint.
    [Fact]
    public async Task Followup_passes_history_appends_and_omits_hint()
    {
        var h = Build("A Toyota.");
        var id = h.Media.Save(new GeneratedMedia([9], "image/png"));
        h.State.AppendVisionTurn("u1", id, new VisionTurn("what is this?", "a car"), 8); // pre-existing first turn

        await h.Proc.ProcessAsync(Item(id, text: "what brand?"), CancellationToken.None);

        var turn = Assert.Single(h.Vision.LastHistory);
        Assert.Equal("what is this?", turn.Question);
        Assert.Equal("a car", turn.Answer);
        Assert.Equal("A Toyota.", Assert.Single(h.Msg.Pushes)); // no hint on a follow-up turn
        Assert.NotNull(Assert.Single(h.Msg.PushQuickReplies));
        // Both turns are now retained.
        var history = h.State.GetVisionHistory("u1", id);
        Assert.Equal(2, history.Count);
        Assert.Equal("what brand?", history[1].Question);
        Assert.Equal("A Toyota.", history[1].Answer);
    }

    // AC5: VisionMaxTurns bounds the retained history (oldest dropped).
    [Fact]
    public async Task History_is_capped_at_VisionMaxTurns()
    {
        var h = Build("answer-N", new AppOptions { VisionMaxTurns = 2 });
        var id = h.Media.Save(new GeneratedMedia([1], "image/png"));
        h.State.AppendVisionTurn("u1", id, new VisionTurn("q1", "a1"), 2);
        h.State.AppendVisionTurn("u1", id, new VisionTurn("q2", "a2"), 2);

        await h.Proc.ProcessAsync(Item(id, text: "q3"), CancellationToken.None);

        var history = h.State.GetVisionHistory("u1", id);
        Assert.Equal(2, history.Count);
        Assert.Equal("q2", history[0].Question); // q1 dropped
        Assert.Equal("q3", history[1].Question);
    }

    // AC7: a first-turn timeout/empty answer is not accumulated and does not open a session (en + ja).
    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public async Task First_turn_failure_does_not_open_session(string locale)
    {
        var app = new AppOptions { Locale = locale };
        var messages = new UserMessages(Options.Create(app));
        var h = Build(messages.Timeout, app); // service returns the localized timeout string
        var id = h.Media.Save(new GeneratedMedia([1], "image/png"));

        await h.Proc.ProcessAsync(Item(id), CancellationToken.None);

        Assert.Equal(messages.Timeout, Assert.Single(h.Msg.Pushes)); // no hint appended
        Assert.NotNull(Assert.Single(h.Msg.PushQuickReplies));        // quick reply still offered
        Assert.False(h.State.Get("u1").VisionActive);                 // session not opened
        Assert.Empty(h.State.GetVisionHistory("u1", id));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public async Task Empty_answer_first_turn_does_not_open_session(string locale)
    {
        var app = new AppOptions { Locale = locale };
        var messages = new UserMessages(Options.Create(app));
        var h = Build(messages.EmptyAnswer, app);
        var id = h.Media.Save(new GeneratedMedia([1], "image/png"));

        await h.Proc.ProcessAsync(Item(id), CancellationToken.None);

        Assert.Equal(messages.EmptyAnswer, Assert.Single(h.Msg.Pushes));
        Assert.False(h.State.Get("u1").VisionActive);
    }

    // AC6: a follow-up whose RefImageId differs from the session image gets an empty history (context reset).
    [Fact]
    public async Task Different_image_resets_history()
    {
        var h = Build("fresh answer");
        var oldId = h.Media.Save(new GeneratedMedia([1], "image/png"));
        var newId = h.Media.Save(new GeneratedMedia([2], "image/png"));
        h.State.AppendVisionTurn("u1", oldId, new VisionTurn("q-old", "a-old"), 8);

        await h.Proc.ProcessAsync(Item(newId, text: "about the new one"), CancellationToken.None);

        Assert.Empty(h.Vision.LastHistory); // no carry-over from the previous image
        Assert.Equal(newId, h.State.Get("u1").VisionImageId); // session switched to the new image
    }

    // AC11: an expired reference image notifies and clears any session; vision is not called.
    [Fact]
    public async Task Expired_reference_image_notifies_clears_session_and_does_not_call_vision()
    {
        var h = Build("unused");
        // Simulate an active session about a now-missing image.
        h.State.AppendVisionTurn("u1", "missing-id", new VisionTurn("q", "a"), 8);
        Assert.True(h.State.Get("u1").VisionActive);

        await h.Proc.ProcessAsync(Item("missing-id"), CancellationToken.None);

        Assert.Equal(0, h.Vision.Calls);
        Assert.Equal(h.Messages.VisionImageExpired, Assert.Single(h.Msg.Replies));
        Assert.Empty(h.Msg.Pushes);
        Assert.False(h.State.Get("u1").VisionActive); // session cleared
    }

    // LINE may redeliver the question event; the same event id must not trigger a second vision call.
    [Fact]
    public async Task Duplicate_event_is_skipped()
    {
        var h = Build("answer");
        var id = h.Media.Save(new GeneratedMedia([9], "image/png"));

        await h.Proc.ProcessAsync(Item(id, eventId: "dup-1"), CancellationToken.None);
        await h.Proc.ProcessAsync(Item(id, eventId: "dup-1"), CancellationToken.None);

        Assert.Equal(1, h.Vision.Calls);
        Assert.Single(h.Msg.Pushes);
    }
}
