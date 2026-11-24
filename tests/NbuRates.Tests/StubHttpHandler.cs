using System.Net;
using System.Text;

namespace NbuRates.Tests;

/// <summary>Підмінний HTTP-обробник: тести не ходять у мережу.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        return respond(request, ct);
    }

    public static StubHttpHandler Returns(HttpStatusCode status, string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    public static StubHttpHandler Throws(Exception ex) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(ex));

    /// <summary>Ніколи не відповідає — для перевірки тайм-ауту.</summary>
    public static StubHttpHandler Hangs() =>
        new(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
}
