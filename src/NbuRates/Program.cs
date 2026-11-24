using System.Text;
using Microsoft.Data.Sqlite;
using NbuRates;
using NbuRates.Api;
using NbuRates.Config;
using NbuRates.Data;

Console.OutputEncoding = Encoding.UTF8;

// 1. Налаштування — з appsettings.json / змінних середовища, не з коду.
AppConfig config;
try
{
    config = AppConfig.Load();
}
catch (Exception ex)
{
    ConsoleUi.Error($"Помилка налаштувань: {ex.Message}");
    return 2;
}

// 2. Один HttpClient на весь застосунок.
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(config.Api.TimeoutSeconds) };
http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
http.DefaultRequestHeaders.UserAgent.ParseAdd("NbuRates/1.0");

var api = new NbuApiClient(http, config.Api.BaseUrl);
var repository = new RatesRepository(config.ConnectionString);

// 3. Створюємо таблиці, якщо їх ще немає.
try
{
    repository.EnsureCreated();
}
catch (SqliteException ex)
{
    ConsoleUi.Error($"Не вдалося відкрити базу даних: {ex.Message}");
    return 3;
}

// 4. Меню (без аргументів) або одна команда (fetch, list, …).
var app = new App(api, repository, config.App);
return args.Length == 0
    ? await app.RunInteractiveAsync()
    : await app.RunCommandAsync(args);
