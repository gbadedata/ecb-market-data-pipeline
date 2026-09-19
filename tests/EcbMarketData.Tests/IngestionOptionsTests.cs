using EcbMarketData.Configuration;

namespace EcbMarketData.Tests;

public sealed class IngestionOptionsTests
{
    [Fact]
    public void Parse_NormalisesAndDeduplicatesCurrencies()
    {
        IngestionOptions options = IngestionOptions.Parse(
            ["--currencies", "usd, GBP,usd", "--output", "test-output"],
            new DateOnly(2026, 9, 19));

        Assert.Equal(new[] { "GBP", "USD" }, options.Currencies);
        Assert.Equal(new DateOnly(2026, 8, 20), options.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 19), options.EndDate);
        Assert.True(Path.IsPathFullyQualified(options.OutputDirectory));
    }

    [Fact]
    public void Parse_RejectsAnInvertedDateRange()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => IngestionOptions.Parse(
            ["--start", "2026-09-20", "--end", "2026-09-19"]));

        Assert.Contains("on or before", exception.Message, StringComparison.Ordinal);
    }
}
