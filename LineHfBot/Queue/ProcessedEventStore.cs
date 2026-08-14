using Microsoft.Extensions.Caching.Memory;

namespace LineHfBot.Queue;

/// <summary>
/// Tracks handled webhook event ids to make generation idempotent across LINE redeliveries.
/// In-memory with a TTL; used for image/video (chat is cheap enough to skip deduping).
/// </summary>
public sealed class ProcessedEventStore(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    /// <summary>Returns true the first time an id is seen; false for a duplicate.</summary>
    public bool TryMarkNew(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
        {
            return true; // nothing to dedupe on; process it
        }

        var key = $"evt:{eventId}";
        if (cache.TryGetValue(key, out _))
        {
            return false;
        }

        cache.Set(key, true, Ttl);
        return true;
    }
}
