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
}
