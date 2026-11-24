using Microsoft.Extensions.Configuration;

namespace NbuRates.Config;

/// <summary>Налаштування доступу до API НБУ (секція "NbuApi").</summary>
public sealed class NbuApiOptions
{
    public string BaseUrl { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>Налаштування поведінки застосунку (секція "App").</summary>
public sealed class AppOptions
{
    public int MaxPeriodDays { get; set; } = 31;
    public int RequestDelayMs { get; set; } = 250;
    public int ListLimit { get; set; } = 30;
}

public sealed class ConfigException(string message) : Exception(message);

/// <summary>
/// Усі налаштування читаються ззовні, у коді немає ні адреси API, ні рядка підключення.
/// Порядок джерел (наступне перекриває попереднє):
///   1) appsettings.json                — значення за замовчуванням, є в git;
///   2) appsettings.Local.json          — локальні значення/ключі, у .gitignore;
///   3) змінні середовища               — напр. NbuApi__BaseUrl, ConnectionStrings__Sqlite.
/// </summary>
public sealed record AppConfig(NbuApiOptions Api, AppOptions App, string ConnectionString)
{
    public static AppConfig Load()
    {
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var api = config.GetSection("NbuApi").Get<NbuApiOptions>()
                  ?? throw new ConfigException("Відсутня секція NbuApi в appsettings.json.");
        var app = config.GetSection("App").Get<AppOptions>() ?? new AppOptions();
        var connectionString = config.GetConnectionString("Sqlite");

        if (!Uri.TryCreate(api.BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ConfigException($"NbuApi:BaseUrl має бути абсолютною http(s)-адресою, зараз: «{api.BaseUrl}».");
        if (api.TimeoutSeconds is < 1 or > 120)
            throw new ConfigException("NbuApi:TimeoutSeconds має бути від 1 до 120.");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ConfigException("Не задано ConnectionStrings:Sqlite.");
        if (app.MaxPeriodDays < 1 || app.ListLimit < 1 || app.RequestDelayMs < 0)
            throw new ConfigException("Некоректні значення в секції App.");

        return new AppConfig(api, app, connectionString);
    }
}
