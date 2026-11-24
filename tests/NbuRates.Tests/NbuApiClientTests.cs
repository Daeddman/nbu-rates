using System.Net;
using NbuRates.Api;

namespace NbuRates.Tests;

public class NbuApiClientTests
{
    private const string BaseUrl = "https://bank.gov.ua/NBUStatService/v1/statdirectory/exchangenew";
    private static readonly DateOnly Date = new(2026, 11, 20);

    private const string UsdJson =
        """[{"r030":840,"txt":"Долар США","rate":41.7787,"cc":"USD","exchangedate":"20.11.2026"}]""";

    private static (NbuApiClient Client, StubHttpHandler Handler) Create(StubHttpHandler handler, int timeoutMs = 5000)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
        return (new NbuApiClient(http, BaseUrl), handler);
    }

    [Fact]
    public async Task ValidJson_IsParsedIntoRate()
    {
        var (client, _) = Create(StubHttpHandler.Returns(HttpStatusCode.OK, UsdJson));

        var rates = await client.GetRatesAsync("USD", Date);

        var rate = Assert.Single(rates);
        Assert.Equal("USD", rate.Code);
        Assert.Equal(840, rate.R030);
        Assert.Equal("Долар США", rate.Name);
        Assert.Equal(41.7787, rate.Rate);
        Assert.Equal(Date, rate.Date);
    }

    [Fact]
    public async Task CurrencyAndDate_AreSentAsQueryParameters()
    {
        var (client, handler) = Create(StubHttpHandler.Returns(HttpStatusCode.OK, UsdJson));

        await client.GetRatesAsync("eur", Date);

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("valcode=eur", query);
        Assert.Contains("date=20261120", query);
        Assert.Contains("json", query);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
    }

    [Fact]
    public void WithoutCurrency_ValcodeIsOmitted()
    {
        var (client, _) = Create(StubHttpHandler.Returns(HttpStatusCode.OK, UsdJson));

        var url = client.BuildUrl(null, Date);

        Assert.DoesNotContain("valcode", url);
        Assert.Equal(BaseUrl + "?json&date=20261120", url);
    }

    [Fact]
    public void CurrencyCode_IsUrlEncoded()
    {
        var (client, _) = Create(StubHttpHandler.Returns(HttpStatusCode.OK, UsdJson));

        var url = client.BuildUrl("a&b=c", Date);

        Assert.EndsWith("valcode=a%26b%3Dc", url);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task StatusOtherThan200_ThrowsHttpStatus(HttpStatusCode status)
    {
        var (client, _) = Create(StubHttpHandler.Returns(status, UsdJson));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetRatesAsync("USD", Date));

        Assert.Equal(ApiErrorKind.HttpStatus, ex.Kind);
        Assert.Equal(status, ex.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task EmptyBody_ThrowsEmptyResponse(string body)
    {
        var (client, _) = Create(StubHttpHandler.Returns(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetRatesAsync("USD", Date));

        Assert.Equal(ApiErrorKind.EmptyResponse, ex.Kind);
    }

    [Fact]
    public async Task EmptyArray_ThrowsNoData()
    {
        var (client, _) = Create(StubHttpHandler.Returns(HttpStatusCode.OK, "[]"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetRatesAsync("XYZ", Date));

        Assert.Equal(ApiErrorKind.NoData, ex.Kind);
        Assert.Contains("XYZ", ex.Message);
    }

    [Theory]
    [InlineData("<exchange><currency/></exchange>")]
    [InlineData("""{"error":"bad request"}""")]
    [InlineData("""[{"r030":840,"rate":"abc"}]""")]
    [InlineData("[{")]
    public async Task NotExpectedJson_ThrowsInvalidJson(string body)
    {
        var (client, _) = Create(StubHttpHandler.Returns(HttpStatusCode.OK, body));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetRatesAsync("USD", Date));

        Assert.Equal(ApiErrorKind.InvalidJson, ex.Kind);
    }

    [Fact]
    public async Task InvalidItems_AreSkipped()
    {
        const string body = """
            [
              {"r030":840,"txt":"Долар США","rate":41.7787,"cc":"USD","exchangedate":"20.11.2026"},
              {"r030":978,"txt":"Євро","rate":48.1,"cc":"","exchangedate":"20.11.2026"},
              {"r030":985,"txt":"Злотий","rate":11.4,"cc":"PLN","exchangedate":"not a date"},
              {"r030":826,"txt":"Фунт","rate":0,"cc":"GBP","exchangedate":"20.11.2026"}
            ]
            """;
        var (client, _) = Create(StubHttpHandler.Returns(HttpStatusCode.OK, body));

        var rates = await client.GetRatesAsync(null, Date);

        Assert.Equal("USD", Assert.Single(rates).Code);
    }

    [Fact]
    public async Task NoNetwork_ThrowsNetwork()
    {
        var (client, _) = Create(StubHttpHandler.Throws(new HttpRequestException("Name or service not known")));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetRatesAsync("USD", Date));

        Assert.Equal(ApiErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task ServerNotResponding_ThrowsTimeout()
    {
        var (client, _) = Create(StubHttpHandler.Hangs(), timeoutMs: 100);

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetRatesAsync("USD", Date));

        Assert.Equal(ApiErrorKind.Timeout, ex.Kind);
    }
}
