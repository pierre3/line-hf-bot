using System.Collections.Concurrent;

namespace LineHfBot.State;

/// <summary>The interaction mode a plain (non-command) message is interpreted as.</summary>
public enum ChatMode
{
    Chat,
    Image,
    Video,
}

/// <summary>
/// Per-user interaction state kept in memory: the current mode plus the last image session
/// (prompt and media id) used by regenerate/edit. Reset on <c>/reset</c> and lost on restart.
/// </summary>
public sealed class UserStateStore
{
    // One mutable state object per user; guarded by locking the object itself.
    private sealed class UserState
    {
        public ChatMode Mode;
        public bool AwaitingEdit;
        public string? LastPrompt;
        public string? LastImageId;
    }

    /// <summary>Immutable snapshot of a user's state for reads.</summary>
    public readonly record struct Snapshot(ChatMode Mode, bool AwaitingEdit, string? LastPrompt, string? LastImageId);

    private readonly ConcurrentDictionary<string, UserState> _byUser = new();

    /// <summary>Current mode; Chat for users with no state yet.</summary>
    public ChatMode GetMode(string userId)
    {
        if (!_byUser.TryGetValue(userId, out var s))
        {
            return ChatMode.Chat;
        }
        lock (s)
        {
            return s.Mode;
        }
    }

    public void SetMode(string userId, ChatMode mode)
    {
        var s = _byUser.GetOrAdd(userId, static _ => new UserState());
        lock (s)
        {
            s.Mode = mode;
        }
    }

    /// <summary>Record the most recent image generation for regenerate/edit.</summary>
    public void SetLastImage(string userId, string prompt, string imageId)
    {
        var s = _byUser.GetOrAdd(userId, static _ => new UserState());
        lock (s)
        {
            s.LastPrompt = prompt;
            s.LastImageId = imageId;
        }
    }

    /// <summary>Update only the last image id (e.g. after an edit) so further edits chain on the new result,
    /// while keeping LastPrompt so regenerate still uses the original text-to-image prompt.</summary>
    public void SetLastImageId(string userId, string imageId)
    {
        var s = _byUser.GetOrAdd(userId, static _ => new UserState());
        lock (s)
        {
            s.LastImageId = imageId;
        }
    }

    /// <summary>
    /// Record a user-sent image as the working image and arm the edit flow in one atomic update:
    /// LastImageId = the stored media id, LastPrompt cleared (regenerate has no prompt to reuse),
    /// and AwaitingEdit = true so the next plain text is taken as the edit instruction. Single lock
    /// so a concurrent worker never sees a half-updated state.
    /// </summary>
    public void SetReceivedImage(string userId, string imageId)
    {
        var s = _byUser.GetOrAdd(userId, static _ => new UserState());
        lock (s)
        {
            s.LastImageId = imageId;
            s.LastPrompt = null;
            s.AwaitingEdit = true;
        }
    }

    /// <summary>Set/clear the "next text is an edit instruction" flag (used by 3b image editing).</summary>
    public void SetAwaitingEdit(string userId, bool awaiting)
    {
        var s = _byUser.GetOrAdd(userId, static _ => new UserState());
        lock (s)
        {
            s.AwaitingEdit = awaiting;
        }
    }

    public Snapshot Get(string userId)
    {
        if (!_byUser.TryGetValue(userId, out var s))
        {
            return new Snapshot(ChatMode.Chat, false, null, null);
        }
        lock (s)
        {
            return new Snapshot(s.Mode, s.AwaitingEdit, s.LastPrompt, s.LastImageId);
        }
    }

    /// <summary>Clear a user's mode and image session (back to defaults).</summary>
    public void Reset(string userId) => _byUser.TryRemove(userId, out _);
}
