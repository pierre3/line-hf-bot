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
        Assert.False(s.AwaitingEdit);
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

    [Fact]
    public void AwaitingEdit_can_be_set_and_cleared()
    {
        var store = new UserStateStore();
        store.SetAwaitingEdit("u1", true);
        Assert.True(store.Get("u1").AwaitingEdit);

        store.SetAwaitingEdit("u1", false);
        Assert.False(store.Get("u1").AwaitingEdit);
    }

    // A user-sent image becomes the working image: id set, prompt cleared, edit armed — all at once.
    [Fact]
    public void SetReceivedImage_sets_id_clears_prompt_and_arms_edit()
    {
        var store = new UserStateStore();
        store.SetLastImage("u1", "an old prompt", "old-img"); // pre-existing generation

        store.SetReceivedImage("u1", "recv-1");

        var s = store.Get("u1");
        Assert.Equal("recv-1", s.LastImageId);
        Assert.Null(s.LastPrompt);
        Assert.True(s.AwaitingEdit);
    }

    [Fact]
    public void Reset_clears_mode_and_image_session()
    {
        var store = new UserStateStore();
        store.SetMode("u1", ChatMode.Image);
        store.SetLastImage("u1", "a cat", "img-1");
        store.SetAwaitingEdit("u1", true);

        store.Reset("u1");

        var s = store.Get("u1");
        Assert.Equal(ChatMode.Chat, s.Mode);
        Assert.False(s.AwaitingEdit);
        Assert.Null(s.LastPrompt);
        Assert.Null(s.LastImageId);
    }
}
