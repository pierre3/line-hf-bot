using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace LineHfBot.Tests;

/// <summary>
/// Test double for HttpClient. Responds via the supplied factory and records, at send time,
/// each request's method / URI / whether it carried an Authorization header (captured before
/// the caller disposes the request message).
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<(HttpMethod Method, Uri? Uri, bool HasAuthorization)> Seen { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Seen.Add((request.Method, request.RequestUri, request.Headers.Authorization is not null));
        return Task.FromResult(responder(request));
    }

    public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage Bytes(byte[] data, string contentType)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
