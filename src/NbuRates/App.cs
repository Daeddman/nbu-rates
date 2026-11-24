using System.Globalization;
using Microsoft.Data.Sqlite;
using NbuRates.Api;
using NbuRates.Config;
using NbuRates.Data;

namespace NbuRates;

/// <summary>Сценарії застосунку: меню або команди з командного рядка.</summary>
public sealed class App(NbuApiClient api, RatesRepository repository, AppOptions options)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ───────────────────────────── інтерактивне меню ─────────────────────────────

    public async Task<int> RunInteractiveAsync()
    {
        ConsoleUi.Title("Офіційні курси НБУ  →  SQLite");
        ConsoleUi.Info($"БД: {repository.DataSourcePath}");

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("  1  Курс валюти на дату           API → екран → БД");
            Console.WriteLine("  2  Усі курси на дату             API → екран → БД");
            Console.WriteLine("  3  Курс валюти за період         API → екран → БД");
            Console.WriteLine("  4  Збережені курси               БД → екран");
            Console.WriteLine("  5  Історія валюти зі зміною      БД → екран");
            Console.WriteLine("  6  Статистика по валютах         БД → екран");
            Console.WriteLine("  0  Вихід");

            var choice = ConsoleUi.Ask("Оберіть пункт");
            if (choice is null or "0")
                return 0;

            try
            {
                switch (choice)
                {
                    case "1":
                        await FetchOneAsync(AskCode(), AskDate("Дата (Enter — сьогодні)"));
                        break;
                    case "2":
                        await FetchAllAsync(AskDate("Дата (Enter — сьогодні)"));
                        break;
                    case "3":
                        var code = AskCode();
                        var from = AskDate("Від дати");
                        var to = AskDate("До дати (Enter — сьогодні)");
                        await FetchPeriodAsync(code, from, to);
                        break;
                    case "4":
                        var filter = ConsoleUi.Ask("Код валюти (Enter — усі)");
                        ShowStored(string.IsNullOrEmpty(filter) ? null : Input.ParseCode(filter));
                        break;
                    case "5":
                        ShowHistory(AskCode());
                        break;
                    case "6":
                        ShowStats();
                        break;
                    default:
                        ConsoleUi.Warn("Такого пункту немає.");
                        break;
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                ConsoleUi.Error(Describe(ex));
            }
        }
    }

    private static string AskCode() =>
        Input.ParseCode(ConsoleUi.Ask("Код валюти (USD, EUR, PLN…)") ?? throw new UserInputException("Введення перервано."));

    private static DateOnly AskDate(string prompt) =>
        Input.ParseDate(ConsoleUi.Ask(prompt), Input.Today);

    // ───────────────────────────── команди CLI ─────────────────────────────

    public async Task<int> RunCommandAsync(string[] args)
    {
        try
        {
            var today = Input.Today;
            switch (args[0].ToLowerInvariant())
            {
                case "fetch":
                    RequireArgs(args, 2, "fetch <КОД> [ДАТА]");
                    await FetchOneAsync(Input.ParseCode(args[1]), Input.ParseDate(args.ElementAtOrDefault(2), today));
                    return 0;
                case "fetch-all":
                    await FetchAllAsync(Input.ParseDate(args.ElementAtOrDefault(1), today));
                    return 0;
                case "period":
                    RequireArgs(args, 3, "period <КОД> <ВІД> [ДО]");
                    await FetchPeriodAsync(Input.ParseCode(args[1]),
                        Input.ParseDate(args[2], today), Input.ParseDate(args.ElementAtOrDefault(3), today));
                    return 0;
                case "list":
                    ShowStored(args.Length > 1 ? Input.ParseCode(args[1]) : null);
                    return 0;
                case "history":
                    RequireArgs(args, 2, "history <КОД>");
                    ShowHistory(Input.ParseCode(args[1]));
                    return 0;
                case "stats":
                    ShowStats();
                    return 0;
                case "help" or "--help" or "-h" or "/?":
                    PrintHelp();
                    return 0;
                default:
                    ConsoleUi.Error($"Невідома команда «{args[0]}».");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ConsoleUi.Error(Describe(ex));
            return 1;
        }
    }

    private static void RequireArgs(string[] args, int count, string usage)
    {
        if (args.Length < count)
            throw new UserInputException($"Замало аргументів. Використання: {usage}");
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Використання: NbuRates [команда] [аргументи]
              (без аргументів)          інтерактивне меню
              fetch <КОД> [ДАТА]        курс валюти на дату: API → екран → БД
              fetch-all [ДАТА]          усі валюти на дату:  API → екран → БД
              period <КОД> <ВІД> [ДО]   курс за кожен день періоду
              list [КОД]                збережені курси з БД
              history <КОД>             історія валюти зі зміною за день
              stats                     статистика (COUNT/MIN/MAX/AVG)
            ДАТА: РРРР-ММ-ДД або ДД.ММ.РРРР; без дати — сьогодні.
            """);
    }

    // ───────────────────────────── сценарії ─────────────────────────────

    private async Task FetchOneAsync(string code, DateOnly date)
    {
        ConsoleUi.Info($"GET {api.BuildUrl(code, date)}");
        var rates = await api.GetRatesAsync(code, date);
        PrintFetched(rates);
        PrintSaved(repository.Save(rates, DateTime.UtcNow));
    }

    private async Task FetchAllAsync(DateOnly date)
    {
        ConsoleUi.Info($"GET {api.BuildUrl(null, date)}");
        var rates = await api.GetRatesAsync(null, date);
        PrintFetched(rates.OrderBy(r => r.Code).ToList());
        PrintSaved(repository.Save(rates, DateTime.UtcNow));
    }

    private async Task FetchPeriodAsync(string code, DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new UserInputException("Кінцева дата раніша за початкову.");
        var days = to.DayNumber - from.DayNumber + 1;
        if (days > options.MaxPeriodDays)
            throw new UserInputException(
                $"Період {days} дн. задовгий: максимум {options.MaxPeriodDays} (налаштування App:MaxPeriodDays).");

        ConsoleUi.Info($"Запитів до API: {days} — GET {api.BuildUrl(code, from)} … date={to.ToString("yyyyMMdd", Inv)}");
        var rows = new List<string[]>();
        int inserted = 0, existing = 0, missing = 0;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            try
            {
                var rates = await api.GetRatesAsync(code, date);
                var saved = repository.Save(rates, DateTime.UtcNow);
                inserted += saved.Inserted;
                existing += saved.Existing;
                rows.Add([rates[0].Date.ToString("yyyy-MM-dd", Inv), FormatRate(rates[0].Rate),
                          saved.Inserted > 0 ? "новий" : "вже був"]);
            }
            catch (ApiException ex) when (ex.Kind == ApiErrorKind.NoData)
            {
                missing++;
                rows.Add([date.ToString("yyyy-MM-dd", Inv), "—", "немає даних"]);
            }

            if (date < to && options.RequestDelayMs > 0)
                await Task.Delay(options.RequestDelayMs);
        }

        ConsoleUi.Table(["Дата", $"{code}, грн", "У БД"], rows, 1);
        ConsoleUi.Success($"Нових записів: {inserted}; уже були (оновлено): {existing}; без даних: {missing}.");
    }

    private void ShowStored(string? code)
    {
        var rows = repository.GetRates(code, null, options.ListLimit);
        if (rows.Count == 0)
        {
            ConsoleUi.Warn(code is null
                ? "У БД ще немає курсів. Спершу отримайте їх з API (пункти 1–3)."
                : $"У БД немає курсів {code}.");
            return;
        }

        ConsoleUi.Table(
            ["Дата", "Код", "Валюта", "Курс, грн", "Отримано"],
            rows.Select(r => new[]
            {
                r.Date.ToString("yyyy-MM-dd", Inv), r.Code, r.Name, FormatRate(r.Rate),
                r.FetchedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Inv),
            }).ToList(),
            3);

        var (currencies, rates) = repository.GetCounts();
        ConsoleUi.Info($"Показано {rows.Count} (ліміт {options.ListLimit}). У БД: валют — {currencies}, курсів — {rates}.");
    }

    private void ShowHistory(string code)
    {
        var rows = repository.GetHistory(code, options.ListLimit);
        if (rows.Count == 0)
        {
            ConsoleUi.Warn($"У БД немає курсів {code}. Отримайте їх пунктом 1 або 3.");
            return;
        }

        ConsoleUi.Table(
            ["Дата", $"{code}, грн", "Зміна"],
            rows.Select(r => new[]
            {
                r.Date.ToString("yyyy-MM-dd", Inv), FormatRate(r.Rate),
                r.Change is { } c ? c.ToString("+0.0000;−0.0000;0.0000", Inv) : "—",
            }).ToList(),
            1, 2);
    }

    private void ShowStats()
    {
        var stats = repository.GetStats();
        if (stats.Count == 0)
        {
            ConsoleUi.Warn("У БД ще немає курсів.");
            return;
        }

        ConsoleUi.Table(
            ["Код", "Валюта", "Днів", "Мін", "Макс", "Середній", "Період"],
            stats.Select(s => new[]
            {
                s.Code, s.Name, s.Count.ToString(Inv), FormatRate(s.Min), FormatRate(s.Max), FormatRate(s.Avg),
                $"{s.First:dd.MM.yyyy} – {s.Last:dd.MM.yyyy}",
            }).ToList(),
            2, 3, 4, 5);
    }

    // ───────────────────────────── допоміжне ─────────────────────────────

    private static void PrintFetched(IReadOnlyList<CurrencyRate> rates) =>
        ConsoleUi.Table(
            ["Код", "Валюта", "Дата", "Курс, грн"],
            rates.Select(r => new[]
            {
                r.Code, r.Name, r.Date.ToString("dd.MM.yyyy", Inv), FormatRate(r.Rate),
            }).ToList(),
            3);

    private static void PrintSaved(SaveResult result) =>
        ConsoleUi.Success($"Збережено в БД: нових записів — {result.Inserted}, уже були (оновлено) — {result.Existing}.");

    private static string FormatRate(double value) => value.ToString("0.0000", Inv);

    private static bool IsExpected(Exception ex) => ex is ApiException or UserInputException or SqliteException;

    private static string Describe(Exception ex) => ex switch
    {
        ApiException apiError => $"Помилка API: {apiError.Message}",
        SqliteException dbError => $"Помилка бази даних: {dbError.Message}",
        _ => ex.Message,
    };
}
