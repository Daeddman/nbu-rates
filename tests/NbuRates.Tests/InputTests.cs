namespace NbuRates.Tests;

public class InputTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    [Theory]
    [InlineData("USD", "USD")]
    [InlineData(" eur ", "EUR")]
    [InlineData("Pln", "PLN")]
    public void ParseCode_AcceptsThreeLatinLetters(string input, string expected) =>
        Assert.Equal(expected, Input.ParseCode(input));

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData("ДОЛ")]
    [InlineData("1$3")]
    [InlineData("' OR '1'='1")]
    public void ParseCode_RejectsEverythingElse(string input) =>
        Assert.Throws<UserInputException>(() => Input.ParseCode(input));

    [Theory]
    [InlineData("2026-11-20")]
    [InlineData("20.11.2026")]
    [InlineData("20261120")]
    public void ParseDate_AcceptsSupportedFormats(string input) =>
        Assert.Equal(new DateOnly(2026, 11, 20), Input.ParseDate(input, Today));

    [Fact]
    public void ParseDate_EmptyMeansToday() =>
        Assert.Equal(Today, Input.ParseDate("  ", Today));

    [Theory]
    [InlineData("20/11/2026")]
    [InlineData("2026-13-01")]
    [InlineData("вчора")]
    public void ParseDate_RejectsInvalid(string input) =>
        Assert.Throws<UserInputException>(() => Input.ParseDate(input, Today));
}
