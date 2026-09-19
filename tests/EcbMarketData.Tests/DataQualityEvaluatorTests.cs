using EcbMarketData.Configuration;
using EcbMarketData.Domain;
using EcbMarketData.Pipeline;

namespace EcbMarketData.Tests;

public sealed class DataQualityEvaluatorTests
{
    private static readonly DateTimeOffset IngestedAt =
        new(2026, 9, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_PassesCompleteUniquePositiveRates()
    {
        IngestionOptions options = CreateOptions(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15));
        FxObservation[] observations =
        [
            CreateObservation(new DateOnly(2026, 9, 14), 0.8650m),
            CreateObservation(new DateOnly(2026, 9, 15), 0.8675m),
        ];

        DataQualityReport report = new DataQualityEvaluator().Evaluate(observations, options, IngestedAt);

        Assert.True(report.Passed);
        Assert.Empty(report.Issues);
        CurrencyQualityMetric metric = Assert.Single(report.CurrencyMetrics);
        Assert.Equal(1m, metric.WeekdayCoverageRatio);
    }

    [Fact]
    public void Evaluate_FailsDuplicateBusinessKeysAndNonPositiveRates()
    {
        IngestionOptions options = CreateOptions(new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 15));
        FxObservation[] observations =
        [
            CreateObservation(new DateOnly(2026, 9, 15), 0m),
            CreateObservation(new DateOnly(2026, 9, 15), 0.8675m),
        ];

        DataQualityReport report = new DataQualityEvaluator().Evaluate(observations, options, IngestedAt);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Rule == "positive_rate");
        Assert.Contains(report.Issues, issue => issue.Rule == "unique_business_key");
    }

    private static IngestionOptions CreateOptions(DateOnly start, DateOnly end) => new(
        start,
        end,
        ["GBP"],
        Path.GetFullPath("test-output"),
        new Uri("https://data-api.ecb.europa.eu/"),
        7);

    private static FxObservation CreateObservation(DateOnly date, decimal rate) => new(
        date,
        "EUR",
        "GBP",
        rate,
        "A",
        "EXR.D.GBP.EUR.SP00.A",
        IngestedAt);
}
