using LineHfBot.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LineHfBot.Media;

/// <summary>Generated media held in memory (bytes + content type).</summary>
public sealed record GeneratedMedia(byte[] Bytes, string ContentType);

/// <summary>
/// In-memory store for generated media, served back at /media/{id} until the TTL expires.
/// LINE requires a public HTTPS URL for image/video messages, so we host the bytes ourselves.
/// Ids are GUIDs to prevent guessing/enumeration.
/// </summary>
public sealed class MediaStore(IMemoryCache cache, IOptions<AppOptions> options)
{
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(Math.Max(1, options.Value.MediaTtlMinutes));

    public string Save(GeneratedMedia media)
    {
        var id = Guid.NewGuid().ToString("N");
        cache.Set(Key(id), media, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl });
        return id;
    }

    public bool TryGet(string id, out GeneratedMedia? media) => cache.TryGetValue(Key(id), out media);

    private static string Key(string id) => $"media:{id}";
}
