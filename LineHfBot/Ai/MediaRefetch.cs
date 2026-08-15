namespace LineHfBot.Ai;

/// <summary>
/// Re-fetches media bytes from a provider-supplied URL when a generation response returns JSON
/// (rather than raw bytes). Applies SSRF defenses shared by the image and video paths:
/// https-only, a host allowlist with label-boundary matching, and no Authorization header
/// (so credentials are never sent to a third-party host).
/// </summary>
internal static class MediaRefetch
{
    private static readonly char[] HostSeparators = [';', ',', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// Validates <paramref name="url"/> against the allowlist, then GETs the bytes (no auth header).
    /// The caller's <paramref name="cancellationToken"/> already carries the per-request timeout budget.
    /// Throws <see cref="InvalidOperationException"/> on a rejected scheme/host and
    /// <see cref="HttpRequestException"/> on a non-success re-fetch.
    /// </summary>
    public static async Task<(byte[] Bytes, string? ContentType)> FetchAsync(
        HttpClient http, string url, IReadOnlyCollection<string> allowedHosts, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Provider media URL is not an absolute URI.");
        }
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to fetch media over non-https scheme '{uri.Scheme}'.");
        }
        if (!IsHostAllowed(uri.Host, allowedHosts))
        {
            throw new InvalidOperationException($"Media host '{uri.Host}' is not in the re-fetch allowlist.");
        }

        // Deliberately no Authorization header: never send HF credentials to a third-party media host.
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await http.SendAsync(request, cancellationToken);
        await HfHttp.EnsureSuccessAsync(response, cancellationToken);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return (bytes, response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Label-boundary host match: allowed when the host equals an allowlist entry, or ends with
    /// "." + entry. Prevents "evilfal.media" from matching an allowlist of "fal.media" while still
    /// allowing "cdn.fal.media". An empty allowlist denies everything (fail-closed).
    /// </summary>
    public static bool IsHostAllowed(string host, IReadOnlyCollection<string> allowedHosts)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }
        foreach (var entry in allowedHosts)
        {
            var allowed = entry.Trim().TrimEnd('.');
            if (allowed.Length == 0)
            {
                continue;
            }
            if (host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Splits the configured allowlist string (";"/","/whitespace separated) into hosts.</summary>
    public static string[] ParseHosts(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? []
            : configured.Split(HostSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
