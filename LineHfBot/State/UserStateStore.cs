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

/// <summary>One question/answer exchange in a conversational vision session (spec09).</summary>
public readonly record struct VisionTurn(string Question, string Answer);

/// <summary>
/// Per-user interaction state kept in memory: the current mode plus the last image session
/// (prompt and media id) used by regenerate/edit/vision, and the active conversational vision
/// session (image id + accumulated Q&amp;A). Reset on <c>/reset</c> and lost on restart.
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

        // Conversational vision (spec09): the image the session is about (null = no active session)
        // and the accumulated Q&A turns resent to the stateless endpoint on each follow-up.
        public string? VisionImageId;
        public List<VisionTurn>? VisionTurns;
    }

    /// <summary>Immutable snapshot of a user's state for reads. <c>VisionActive</c> is true while a
    /// conversational vision session is open (<c>VisionImageId</c> is the image it is about).</summary>
    public readonly record struct Snapshot(
        ChatMode Mode, PendingAction Pending, string? LastPrompt, string? LastImageId,
        bool VisionActive, string? VisionImageId);

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
            // Switching mode ends any conversational vision session (plain text no longer follows up).
            ClearVision(s);
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
            // A new image supersedes any vision session about the previous one.
            ClearVision(s);
        }
    }

    /// <summary>Update only the last image id (e.g. after an edit) so further edits chain on the new result,
    /// while keeping LastPrompt so regenerate still uses the original text-to-image prompt. Does not touch the
    /// vision session: an edit is always armed via the <c>edit</c> postback, which already cleared it.</summary>
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
            // A newly received image supersedes any vision session about a previous one.
            ClearVision(s);
        }
    }

    /// <summary>Set what the next plain text resolves to (edit / vision question), or None to clear it.
    /// Does not touch the vision session (re-arming <c>ask</c> must preserve it; the worker decides
    /// continue-or-reset by image id).</summary>
    public void SetPending(string userId, PendingAction pending)
    {
        var s = _byUser.GetOrAdd(userId, static _ => new UserState());
        lock (s)
        {
            s.Pending = pending;
        }
    }

    /// <summary>Accumulated Q&amp;A for an active vision session about <paramref name="imageId"/>, or an empty
    /// list when no session is active or it is about a different image (image change resets the context).</summary>
    public IReadOnlyList<VisionTurn> GetVisionHistory(string userId, string imageId)
    {
        if (!_byUser.TryGetValue(userId, out var s))
        {
            return [];
        }
        lock (s)
        {
            if (s.VisionImageId is null || !string.Equals(s.VisionImageId, imageId, StringComparison.Ordinal) ||
                s.VisionTurns is null || s.VisionTurns.Count == 0)
            {
                return [];
            }
            return s.VisionTurns.ToArray(); // copy so callers never see the list mutate under the lock
        }
    }

    /// <summary>Append a successful Q&amp;A turn to the vision session for <paramref name="imageId"/>, opening a
    /// session (or switching subject) if the id differs from the current one. Keeps at most
    /// <paramref name="maxTurns"/> most-recent turns (values below 1 are treated as 1) to bound the image+history
    /// resent on each follow-up.</summary>
    public void AppendVisionTurn(string userId, string imageId, VisionTurn turn, int maxTurns)
    {
        var cap = Math.Max(1, maxTurns);
        var s = _byUser.GetOrAdd(userId, static _ => new UserState());
        lock (s)
        {
            if (!string.Equals(s.VisionImageId, imageId, StringComparison.Ordinal))
            {
                // New subject: start a fresh session for this image.
                s.VisionImageId = imageId;
                s.VisionTurns = [];
            }
            s.VisionTurns ??= [];
            s.VisionTurns.Add(turn);
            if (s.VisionTurns.Count > cap)
            {
                s.VisionTurns.RemoveRange(0, s.VisionTurns.Count - cap);
            }
        }
    }

    /// <summary>End the conversational vision session (no image, no turns).</summary>
    public void ClearVisionSession(string userId)
    {
        if (!_byUser.TryGetValue(userId, out var s))
        {
            return;
        }
        lock (s)
        {
            ClearVision(s);
        }
    }

    private static void ClearVision(UserState s)
    {
        s.VisionImageId = null;
        s.VisionTurns = null;
    }

    public Snapshot Get(string userId)
    {
        if (!_byUser.TryGetValue(userId, out var s))
        {
            return new Snapshot(ChatMode.Chat, PendingAction.None, null, null, false, null);
        }
        lock (s)
        {
            return new Snapshot(s.Mode, s.Pending, s.LastPrompt, s.LastImageId,
                s.VisionImageId is not null, s.VisionImageId);
        }
    }

    /// <summary>Clear a user's mode and image session (back to defaults).</summary>
    public void Reset(string userId) => _byUser.TryRemove(userId, out _);
}
