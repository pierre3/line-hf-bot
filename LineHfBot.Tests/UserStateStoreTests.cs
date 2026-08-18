using LineHfBot.State;

namespace LineHfBot.Tests;

public class UserStateStoreTests
{
    [Fact]
    public void Defaults_are_chat_and_empty()
    {
        var store = new UserStateStore();
        var s = store.Get("u1");
        Assert.Equal(ChatMode.Chat, s.Mode);
        Assert.Equal(PendingAction.None, s.Pending);
        Assert.Null(s.LastPrompt);
        Assert.Null(s.LastImageId);
    }

    // After an edit we chain on the new image id but keep the original prompt so regenerate still uses it.
    [Fact]
    public void SetLastImageId_updates_id_but_keeps_prompt()
    {
        var store = new UserStateStore();
        store.SetLastImage("u1", "a cat", "img-1");

        store.SetLastImageId("u1", "img-2");

        var s = store.Get("u1");
        Assert.Equal("a cat", s.LastPrompt);
        Assert.Equal("img-2", s.LastImageId);
    }

    [Theory]
    [InlineData(PendingAction.Edit)]
    [InlineData(PendingAction.VisionQuestion)]
    public void Pending_can_be_set_and_cleared(PendingAction pending)
    {
        var store = new UserStateStore();
        store.SetPending("u1", pending);
        Assert.Equal(pending, store.Get("u1").Pending);

        store.SetPending("u1", PendingAction.None);
        Assert.Equal(PendingAction.None, store.Get("u1").Pending);
    }

    // A user-sent image becomes the working image: id set, prompt cleared, pending set — all at once.
    // Vision on -> None (user picks edit/ask); vision off -> Edit (spec04 behavior).
    [Theory]
    [InlineData(PendingAction.None)]
    [InlineData(PendingAction.Edit)]
    public void SetReceivedImage_sets_id_clears_prompt_and_sets_pending(PendingAction pending)
    {
        var store = new UserStateStore();
        store.SetLastImage("u1", "an old prompt", "old-img"); // pre-existing generation

        store.SetReceivedImage("u1", "recv-1", pending);

        var s = store.Get("u1");
        Assert.Equal("recv-1", s.LastImageId);
        Assert.Null(s.LastPrompt);
        Assert.Equal(pending, s.Pending);
    }

    [Fact]
    public void Reset_clears_mode_and_image_session()
    {
        var store = new UserStateStore();
        store.SetMode("u1", ChatMode.Image);
        store.SetLastImage("u1", "a cat", "img-1");
        store.SetPending("u1", PendingAction.Edit);

        store.Reset("u1");

        var s = store.Get("u1");
        Assert.Equal(ChatMode.Chat, s.Mode);
        Assert.Equal(PendingAction.None, s.Pending);
        Assert.Null(s.LastPrompt);
        Assert.Null(s.LastImageId);
    }

    // --- Conversational vision session (spec09) ---

    [Fact]
    public void Vision_history_is_empty_by_default_and_session_inactive()
    {
        var store = new UserStateStore();
        Assert.Empty(store.GetVisionHistory("u1", "img-1"));
        Assert.False(store.Get("u1").VisionActive);
        Assert.Null(store.Get("u1").VisionImageId);
    }

    [Fact]
    public void AppendVisionTurn_opens_session_and_accumulates()
    {
        var store = new UserStateStore();
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q1", "a1"), 8);
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q2", "a2"), 8);

        var s = store.Get("u1");
        Assert.True(s.VisionActive);
        Assert.Equal("img-1", s.VisionImageId);
        var history = store.GetVisionHistory("u1", "img-1");
        Assert.Equal(["q1", "q2"], history.Select(t => t.Question));
    }

    // AC5: only the most-recent maxTurns pairs are retained; values below 1 are treated as 1.
    [Fact]
    public void AppendVisionTurn_caps_history_to_maxTurns()
    {
        var store = new UserStateStore();
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q1", "a1"), 2);
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q2", "a2"), 2);
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q3", "a3"), 2);

        Assert.Equal(["q2", "q3"], store.GetVisionHistory("u1", "img-1").Select(t => t.Question));
    }

    [Fact]
    public void AppendVisionTurn_treats_non_positive_maxTurns_as_one()
    {
        var store = new UserStateStore();
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q1", "a1"), 0);
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q2", "a2"), 0);

        var only = Assert.Single(store.GetVisionHistory("u1", "img-1"));
        Assert.Equal("q2", only.Question);
    }

    // AC6: a session about a different image returns an empty history (context does not carry over).
    [Fact]
    public void GetVisionHistory_returns_empty_for_a_different_image()
    {
        var store = new UserStateStore();
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q1", "a1"), 8);

        Assert.Empty(store.GetVisionHistory("u1", "img-2"));
    }

    [Fact]
    public void AppendVisionTurn_switches_subject_when_image_changes()
    {
        var store = new UserStateStore();
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q1", "a1"), 8);
        store.AppendVisionTurn("u1", "img-2", new VisionTurn("q2", "a2"), 8);

        Assert.Equal("img-2", store.Get("u1").VisionImageId);
        Assert.Empty(store.GetVisionHistory("u1", "img-1"));
        Assert.Equal(["q2"], store.GetVisionHistory("u1", "img-2").Select(t => t.Question));
    }

    [Fact]
    public void ClearVisionSession_ends_the_session()
    {
        var store = new UserStateStore();
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q1", "a1"), 8);

        store.ClearVisionSession("u1");

        Assert.False(store.Get("u1").VisionActive);
        Assert.Empty(store.GetVisionHistory("u1", "img-1"));
    }

    // AC10: mode switch, a new generation, and a newly received image all end the session.
    [Theory]
    [InlineData("mode")]
    [InlineData("generate")]
    [InlineData("receive")]
    [InlineData("reset")]
    public void Session_ends_on_state_transitions(string transition)
    {
        var store = new UserStateStore();
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q1", "a1"), 8);

        switch (transition)
        {
            case "mode": store.SetMode("u1", ChatMode.Image); break;
            case "generate": store.SetLastImage("u1", "a dog", "img-2"); break;
            case "receive": store.SetReceivedImage("u1", "img-3", PendingAction.None); break;
            case "reset": store.Reset("u1"); break;
        }

        Assert.False(store.Get("u1").VisionActive);
    }

    // Decision⑤: chaining the last image id (edit result) does NOT clear the session (edit already cleared it).
    [Fact]
    public void SetLastImageId_does_not_end_the_session()
    {
        var store = new UserStateStore();
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q1", "a1"), 8);

        store.SetLastImageId("u1", "img-edited");

        Assert.True(store.Get("u1").VisionActive);
        Assert.Equal("img-1", store.Get("u1").VisionImageId);
    }

    // Re-arming ask (SetPending) must not clear the session (the worker decides continue-or-reset by image id).
    [Fact]
    public void SetPending_does_not_end_the_session()
    {
        var store = new UserStateStore();
        store.AppendVisionTurn("u1", "img-1", new VisionTurn("q1", "a1"), 8);

        store.SetPending("u1", PendingAction.VisionQuestion);

        Assert.True(store.Get("u1").VisionActive);
    }
}
