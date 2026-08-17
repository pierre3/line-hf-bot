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
/// What the next plain (non-command) message resolves to, when the user has been prompted for one.
/// <c>Edit</c> = the text is an image-edit instruction (image-to-image); <c>VisionQuestion</c> = the text
/// is a question about the working image (vision/VQA); <c>Animate</c> = the text is a motion instruction
/// for image-to-video. <c>None</c> = interpret by the current mode. Exactly one action is pending at a time
/// (mode switch / command / regenerate clears it).
/// </summary>
public enum PendingAction
{
    None,
    Edit,
    VisionQuestion,
    Animate,
}

/// <summary>
/// Per-user interaction state kept in memory: the current mode plus the last image session
/// (prompt and media id) used by regenerate/edit/vision. Reset on <c>/reset</c> and lost on restart.
/// </summary>
public sealed class UserStateStore
{
    // One mutable state object per user; guarded by locking the object itself.
    private sealed class UserState
    {
        public ChatMode Mode;
        public PendingAction Pending;
        public string? LastPrompt;
        public string? LastImageId;
    }

    /// <summary>Immutable snapshot of a user's state for reads.</summary>
    public readonly record struct Snapshot(ChatMode Mode, PendingAction Pending, string? LastPrompt, string? LastImageId);

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
    /// Record a user-sent image as the working image in one atomic update: LastImageId = the stored media id,
    /// LastPrompt cleared (regenerate has no prompt to reuse), and Pending set as requested. When vision is
    /// enabled we set <see cref="PendingAction.None"/> and let the user pick edit/ask via quick reply; when
    /// vision is off we set <see cref="PendingAction.Edit"/> so the next plain text edits the image (spec04
    /// behavior). Single lock so a concurrent worker never sees a half-updated state.
    /// </summary>
    public void SetReceivedImage(string userId, string imageId, PendingAction pending)
    {
        var s = _byUser.GetOrAdd(userId, static _ => new UserState());
        lock (s)
        {
            s.LastImageId = imageId;
            s.LastPrompt = null;
            s.Pending = pending;
        }
    }

    /// <summary>Set what the next plain text resolves to (edit / vision question), or None to clear it.</summary>
    public void SetPending(string userId, PendingAction pending)
    {
        var s = _byUser.GetOrAdd(userId, static _ => new UserState());
        lock (s)
        {
            s.Pending = pending;
        }
    }

    public Snapshot Get(string userId)
    {
        if (!_byUser.TryGetValue(userId, out var s))
        {
            return new Snapshot(ChatMode.Chat, PendingAction.None, null, null);
        }
        lock (s)
        {
            return new Snapshot(s.Mode, s.Pending, s.LastPrompt, s.LastImageId);
        }
    }

    /// <summary>Clear a user's mode and image session (back to defaults).</summary>
    public void Reset(string userId) => _byUser.TryRemove(userId, out _);
}
