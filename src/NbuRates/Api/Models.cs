using System.Text.Json.Serialization;

namespace NbuRates.Api;

/// <summary>
/// Один елемент JSON-масиву, який повертає API НБУ:
/// { "r030": 840, "txt": "Долар США", "rate": 41.7787, "cc": "USD", "exchangedate": "20.11.2026" }
/// </summary>
public sealed class NbuRateDto
{
    [JsonPropertyName("r030")] public int R030 { get; set; }
    [JsonPropertyName("txt")] public string? Name { get; set; }
    [JsonPropertyName("rate")] public double Rate { get; set; }
    [JsonPropertyName("cc")] public string? Code { get; set; }
    [JsonPropertyName("exchangedate")] public string? ExchangeDate { get; set; }
}

/// <summary>Перевірений курс, з яким працює решта програми.</summary>
public sealed record CurrencyRate(int R030, string Code, string Name, DateOnly Date, double Rate);
