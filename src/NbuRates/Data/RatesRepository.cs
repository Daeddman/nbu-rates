using System.Globalization;
using Microsoft.Data.Sqlite;
using NbuRates.Api;

namespace NbuRates.Data;

public sealed record SaveResult(int Inserted, int Existing);

public sealed record StoredRate(string Code, string Name, DateOnly Date, double Rate, DateTime FetchedAtUtc);

public sealed record HistoryRow(DateOnly Date, double Rate, double? Change);

public sealed record CurrencyStats(
    string Code, string Name, int Count, double Min, double Max, double Avg, DateOnly First, DateOnly Last);

/// <summary>
/// Робота з SQLite. Дві таблиці:
///   currencies (1) ──&lt; rates (∞),  rates.currency_id → currencies.id (FOREIGN KEY).
/// Усі значення передаються лише через параметри ($name), без конкатенації рядків.
/// </summary>
public sealed class RatesRepository
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS currencies (
            id    INTEGER PRIMARY KEY AUTOINCREMENT,
            r030  INTEGER NOT NULL,            -- числовий код ISO 4217 (840)
            code  TEXT    NOT NULL UNIQUE,     -- літерний код (USD)
            name  TEXT    NOT NULL             -- назва українською
        );

        CREATE TABLE IF NOT EXISTS rates (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            currency_id INTEGER NOT NULL
                        REFERENCES currencies (id) ON DELETE CASCADE,
            rate_date   TEXT    NOT NULL,      -- дата курсу, РРРР-ММ-ДД
            rate        REAL    NOT NULL CHECK (rate > 0),
            fetched_at  TEXT    NOT NULL,      -- коли отримано з API, UTC
            UNIQUE (currency_id, rate_date)    -- один курс валюти на день: без дублікатів
        );

        CREATE INDEX IF NOT EXISTS ix_rates_date ON rates (rate_date);
        """;

    private readonly string _connectionString;

    public RatesRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Порожній рядок підключення.", nameof(connectionString));
        _connectionString = connectionString;
    }

    /// <summary>Повний шлях до файлу БД (для виводу користувачу).</summary>
    public string DataSourcePath
    {
        get
        {
            var source = new SqliteConnectionStringBuilder(_connectionString).DataSource;
            return string.IsNullOrEmpty(source) || source == ":memory:" ? source : Path.GetFullPath(source);
        }
    }

    /// <summary>Відкриває з'єднання і вмикає перевірку зовнішніх ключів (у SQLite вона вимкнена за замовчуванням).</summary>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public void EnsureCreated()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Зберігає курси в одній транзакції. Повторне збереження тих самих даних не створює
    /// нових рядків: UNIQUE + INSERT ... ON CONFLICT DO UPDATE (upsert) лише оновлює курс.
    /// </summary>
    public SaveResult Save(IReadOnlyCollection<CurrencyRate> rates, DateTime fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var fetchedAt = fetchedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        var before = CountRates(connection, transaction);
        foreach (var rate in rates)
        {
            var currencyId = UpsertCurrency(connection, transaction, rate);
            UpsertRate(connection, transaction, currencyId, rate, fetchedAt);
        }
        var after = CountRates(connection, transaction);

        transaction.Commit();

        var inserted = (int)(after - before);
        return new SaveResult(inserted, rates.Count - inserted);
    }

    private static long UpsertCurrency(SqliteConnection connection, SqliteTransaction transaction, CurrencyRate rate)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO currencies (r030, code, name)
            VALUES ($r030, $code, $name)
            ON CONFLICT (code) DO UPDATE SET
                r030 = excluded.r030,
                name = excluded.name
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$r030", rate.R030);
        command.Parameters.AddWithValue("$code", rate.Code);
        command.Parameters.AddWithValue("$name", rate.Name);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void UpsertRate(
        SqliteConnection connection, SqliteTransaction transaction, long currencyId, CurrencyRate rate, string fetchedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO rates (currency_id, rate_date, rate, fetched_at)
            VALUES ($currencyId, $date, $rate, $fetchedAt)
            ON CONFLICT (currency_id, rate_date) DO UPDATE SET
                rate       = excluded.rate,
                fetched_at = excluded.fetched_at;
            """;
        command.Parameters.AddWithValue("$currencyId", currencyId);
        command.Parameters.AddWithValue("$date", rate.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rate", rate.Rate);
        command.Parameters.AddWithValue("$fetchedAt", fetchedAt);
        command.ExecuteNonQuery();
    }

    private static long CountRates(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM rates;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Збережені курси (JOIN двох таблиць). Фільтри необов'язкові: null — без фільтра.</summary>
    public IReadOnlyList<StoredRate> GetRates(string? code, DateOnly? date, int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.code, c.name, r.rate_date, r.rate, r.fetched_at
            FROM rates AS r
            JOIN currencies AS c ON c.id = r.currency_id
            WHERE ($code IS NULL OR c.code = $code)
              AND ($date IS NULL OR r.rate_date = $date)
            ORDER BY r.rate_date DESC, c.code
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$code", (object?)code ?? DBNull.Value);
        command.Parameters.AddWithValue("$date",
            date is { } d ? d.ToString(DateFormat, CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<StoredRate>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new StoredRate(
                Code: reader.GetString(0),
                Name: reader.GetString(1),
                Date: ParseDate(reader.GetString(2)),
                Rate: reader.GetDouble(3),
                FetchedAtUtc: DateTime.ParseExact(reader.GetString(4), TimestampFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)));
        }
        return result;
    }

    /// <summary>Історія курсу валюти зі зміною до попереднього збереженого дня (віконна функція LAG).</summary>
    public IReadOnlyList<HistoryRow> GetHistory(string code, int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.rate_date,
                   r.rate,
                   r.rate - LAG(r.rate) OVER (ORDER BY r.rate_date) AS change
            FROM rates AS r
            JOIN currencies AS c ON c.id = r.currency_id
            WHERE c.code = $code
            ORDER BY r.rate_date DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<HistoryRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new HistoryRow(
                ParseDate(reader.GetString(0)),
                reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2)));
        }
        return result;
    }

    /// <summary>Агрегати по кожній валюті: COUNT, MIN, MAX, AVG + GROUP BY.</summary>
    public IReadOnlyList<CurrencyStats> GetStats()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.code, c.name,
                   COUNT(r.id), MIN(r.rate), MAX(r.rate), AVG(r.rate),
                   MIN(r.rate_date), MAX(r.rate_date)
            FROM currencies AS c
            JOIN rates AS r ON r.currency_id = c.id
            GROUP BY c.id, c.code, c.name
            ORDER BY COUNT(r.id) DESC, c.code;
            """;

        var result = new List<CurrencyStats>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CurrencyStats(
                reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5),
                ParseDate(reader.GetString(6)), ParseDate(reader.GetString(7))));
        }
        return result;
    }

    /// <summary>Кількість рядків у кожній таблиці.</summary>
    public (long Currencies, long Rates) GetCounts()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM currencies), (SELECT COUNT(*) FROM rates);";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);
}
