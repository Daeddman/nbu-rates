using System.Net;

namespace NbuRates.Api;

public enum ApiErrorKind
{
    /// <summary>Немає мережі, DNS не знайшов сервер, з'єднання розірване.</summary>
    Network,
    /// <summary>Сервер не відповів за відведений час.</summary>
    Timeout,
    /// <summary>Код статусу відповіді не 200.</summary>
    HttpStatus,
    /// <summary>Тіло відповіді порожнє.</summary>
    EmptyResponse,
    /// <summary>Тіло не є JSON-масивом очікуваного формату.</summary>
    InvalidJson,
    /// <summary>JSON коректний, але даних немає (порожній масив).</summary>
    NoData,
}

public sealed class ApiException(
    ApiErrorKind kind,
    string message,
    Exception? inner = null,
    HttpStatusCode? statusCode = null) : Exception(message, inner)
{
    public ApiErrorKind Kind { get; } = kind;
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
