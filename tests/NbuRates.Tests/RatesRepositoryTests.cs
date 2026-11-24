using Microsoft.Data.Sqlite;
using NbuRates.Api;
using NbuRates.Data;

namespace NbuRates.Tests;

/// <summary>Кожен тест працює з окремим тимчасовим файлом БД.</summary>
public sealed class RatesRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nbu_test_{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    private readonly RatesRepository _repository;

    private static readonly DateOnly Day1 = new(2026, 11, 19);
    private static readonly DateOnly Day2 = new(2026, 11, 20);
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    public RatesRepositoryTests()
    {
        _connectionString = $"Data Source={_dbPath};Pooling=False";
        _repository = new RatesRepository(_connectionString);
        _repository.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { /* тимчасовий файл — не критично */ }
    }

    private static CurrencyRate Usd(DateOnly date, double rate) => new(840, "USD", "Долар США", date, rate);
    private static CurrencyRate Eur(DateOnly date, double rate) => new(978, "EUR", "Євро", date, rate);

    [Fact]
    public void Schema_HasTwoTablesLinkedByForeignKey()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"table\", \"from\", \"to\" FROM pragma_foreign_key_list('rates');";
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("currencies", reader.GetString(0));
        Assert.Equal("currency_id", reader.GetString(1));
        Assert.Equal("id", reader.GetString(2));
    }

    [Fact]
    public void ForeignKey_RejectsRateForUnknownCurrency()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO rates (currency_id, rate_date, rate, fetched_at) VALUES ($id, '2026-11-20', 1.0, 'x');";
        insert.Parameters.AddWithValue("$id", 999);

        var ex = Assert.Throws<SqliteException>(() => insert.ExecuteNonQuery());
        Assert.Equal(19, ex.SqliteErrorCode); // SQLITE_CONSTRAINT
    }

    [Fact]
    public void SavingSameDataTwice_DoesNotCreateDuplicates()
    {
        var first = _repository.Save([Usd(Day2, 41.7787)], Now);
        var second = _repository.Save([Usd(Day2, 41.7787)], Now.AddMinutes(5));

        Assert.Equal(new SaveResult(1, 0), first);
        Assert.Equal(new SaveResult(0, 1), second);
        Assert.Equal((1L, 1L), _repository.GetCounts());
    }

    [Fact]
    public void SavingChangedRate_UpdatesExistingRow()
    {
        _repository.Save([Usd(Day2, 41.0)], Now);
        _repository.Save([Usd(Day2, 41.5)], Now);

        var stored = Assert.Single(_repository.GetRates("USD", Day2, 10));
        Assert.Equal(41.5, stored.Rate);
    }

    [Fact]
    public void OneCurrency_HasManyRates()
    {
        _repository.Save([Usd(Day1, 41.0), Eur(Day1, 48.0)], Now);
        _repository.Save([Usd(Day2, 41.2)], Now);

        Assert.Equal((2L, 3L), _repository.GetCounts());
        Assert.Equal(2, _repository.GetRates("USD", null, 10).Count);
    }

    [Fact]
    public void GetRates_ReturnsCurrencyNameFromJoin_NewestFirst()
    {
        _repository.Save([Usd(Day1, 41.0), Usd(Day2, 41.2), Eur(Day2, 48.0)], Now);

        var usd = _repository.GetRates("USD", null, 10);

        Assert.Equal(new[] { Day2, Day1 }, usd.Select(r => r.Date).ToArray());
        Assert.All(usd, r => Assert.Equal("Долар США", r.Name));
        Assert.Equal(Now, usd[0].FetchedAtUtc);
    }

    [Fact]
    public void GetRates_LimitIsApplied()
    {
        _repository.Save([Usd(Day1, 41.0), Usd(Day2, 41.2), Eur(Day2, 48.0)], Now);

        Assert.Equal(2, _repository.GetRates(null, null, 2).Count);
    }

    [Fact]
    public void SqlInjectionInFilter_IsTreatedAsPlainValue()
    {
        _repository.Save([Usd(Day2, 41.7787)], Now);

        var rows = _repository.GetRates("' OR '1'='1", null, 10);

        Assert.Empty(rows);
        Assert.Equal((1L, 1L), _repository.GetCounts());
    }

    [Fact]
    public void History_ContainsChangeFromPreviousDay()
    {
        _repository.Save([Usd(Day1, 41.0)], Now);
        _repository.Save([Usd(Day2, 41.25)], Now);

        var history = _repository.GetHistory("USD", 10);

        Assert.Equal(2, history.Count);
        Assert.Equal(Day2, history[0].Date);
        Assert.Equal(0.25, history[0].Change!.Value, 6);
        Assert.Null(history[1].Change);
    }

    [Fact]
    public void Stats_AggregatesPerCurrency()
    {
        _repository.Save([Usd(Day1, 41.0), Usd(Day2, 42.0), Eur(Day2, 48.0)], Now);

        var usd = _repository.GetStats().Single(s => s.Code == "USD");

        Assert.Equal(2, usd.Count);
        Assert.Equal(41.0, usd.Min);
        Assert.Equal(42.0, usd.Max);
        Assert.Equal(41.5, usd.Avg);
        Assert.Equal(Day1, usd.First);
        Assert.Equal(Day2, usd.Last);
    }
}
