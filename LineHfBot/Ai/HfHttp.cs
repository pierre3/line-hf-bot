namespace LineHfBot.Ai;

/// <summary>Shared helpers for Hugging Face HTTP calls.</summary>
internal static class HfHttp
{
    /// <summary>
    /// Like EnsureSuccessStatusCode, but includes the response body in the error so the actual
    /// HF reason (e.g. unsupported task/model) is visible in logs and the dev probes.
    /// </summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body;
        try { body = await response.Content.ReadAsStringAsync(cancellationToken); }
        catch { body = ""; }

        if (body.Length > 500)
        {
            body = body[..500];
        }

        throw new HttpRequestException($"HF {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }
}
