using System.Globalization;

namespace NbuRates;

/// <summary>Помилка у введених користувачем даних.</summary>
public sealed class UserInputException(string message) : Exception(message);

/// <summary>Перевірка й розбір того, що ввів користувач.</summary>
public static class Input
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "yyyyMMdd"];

    /// <summary>Код валюти: рівно 3 латинські літери (USD, eur → EUR).</summary>
    public static string ParseCode(string? text)
    {
        var code = (text ?? "").Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(ch => ch is >= 'A' and <= 'Z'))
            throw new UserInputException($"Код валюти — це 3 латинські літери (USD, EUR, PLN…), а не «{text}».");
        return code;
    }

    /// <summary>Дата у форматі РРРР-ММ-ДД, ДД.ММ.РРРР або РРРРММДД. Порожньо — сьогодні.</summary>
    public static DateOnly ParseDate(string? text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
            return today;
        if (DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        throw new UserInputException($"Не вдалося розпізнати дату «{text}». Формат: РРРР-ММ-ДД або ДД.ММ.РРРР.");
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
