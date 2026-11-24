using System.Globalization;
using System.Net;
using System.Text.Json;

namespace NbuRates.Api;

/// <summary>
/// Клієнт API офіційних курсів НБУ.
/// Параметри запиту <c>valcode</c> (код валюти) і <c>date</c> (РРРРММДД) змінюють відповідь.
/// </summary>
public sealed class NbuApiClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    /// <param name="http">Один HttpClient на весь застосунок (створюється в Program.cs).</param>
    /// <param name="baseUrl">Адреса ендпоінта з налаштувань, напр. .../statdirectory/exchangenew.</param>
    public NbuApiClient(HttpClient http, string baseUrl)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Порожня адреса API.", nameof(baseUrl));
        _baseUrl = baseUrl.TrimEnd('/', '?');
    }

    /// <summary>Будує URL із query-параметрами. Без коду валюти API повертає всі валюти на дату.</summary>
    public string BuildUrl(string? currencyCode, DateOnly date)
    {
        var url = $"{_baseUrl}?json&date={date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(currencyCode))
            url += "&valcode=" + Uri.EscapeDataString(currencyCode.Trim());
        return url;
    }

    /// <summary>
    /// GET-запит до API. Будь-яка проблема (мережа, тайм-аут, статус ≠ 200, порожня відповідь,
    /// зламаний JSON, відсутність даних) перетворюється на <see cref="ApiException"/> зі зрозумілим текстом.
    /// </summary>
    public async Task<IReadOnlyList<CurrencyRate>> GetRatesAsync(
        string? currencyCode, DateOnly date, CancellationToken ct = default)
    {
        var url = BuildUrl(currencyCode, date);

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(ApiErrorKind.Timeout,
                $"Сервер не відповів за {_http.Timeout.TotalSeconds:0} с. Спробуйте пізніше.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(ApiErrorKind.Network,
                $"Немає з'єднання з API (перевірте інтернет): {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ApiException(ApiErrorKind.HttpStatus,
                    $"API повернуло статус {(int)response.StatusCode} {response.ReasonPhrase} замість 200 OK.",
                    statusCode: response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                throw new ApiException(ApiErrorKind.EmptyResponse, "API повернуло порожню відповідь.");

            List<NbuRateDto>? items;
            try
            {
                items = JsonSerializer.Deserialize<List<NbuRateDto>>(body);
            }
            catch (JsonException ex)
            {
                throw new ApiException(ApiErrorKind.InvalidJson,
                    "Відповідь API не є JSON-масивом очікуваного формату.", ex);
            }

            var rates = (items ?? [])
                .Select(TryMap)
                .OfType<CurrencyRate>()
                .ToList();

            if (rates.Count == 0)
            {
                var what = string.IsNullOrWhiteSpace(currencyCode) ? "курсів" : $"курсу «{currencyCode}»";
                throw new ApiException(ApiErrorKind.NoData,
                    $"API не повернуло {what} на {date:dd.MM.yyyy}. Перевірте код валюти та дату.");
            }

            return rates;
        }
    }

    /// <summary>Відкидає записи без коду, з некоректною датою чи курсом.</summary>
    private static CurrencyRate? TryMap(NbuRateDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Code) || dto.Rate <= 0 || double.IsNaN(dto.Rate))
            return null;

        if (!DateOnly.TryParseExact(dto.ExchangeDate?.Trim(), "dd.MM.yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        var code = dto.Code.Trim().ToUpperInvariant();
        var name = string.IsNullOrWhiteSpace(dto.Name) ? code : dto.Name.Trim();
        return new CurrencyRate(dto.R030, code, name, date, dto.Rate);
    }
}
