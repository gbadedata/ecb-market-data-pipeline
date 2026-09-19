using EcbMarketData.Configuration;
using EcbMarketData.Domain;
using EcbMarketData.Infrastructure;
using EcbMarketData.Pipeline;

namespace EcbMarketData.Tests;

public sealed class MarketDataPipelineTests
{
    [Fact]
    public async Task RunAsync_IsIdempotentAcrossRepeatedIngestion()
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ecb-market-data-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            string fixture = File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "ecb_fx_sample.csv"));
            DateTimeOffset fixedTime = new(2026, 9, 16, 8, 30, 0, TimeSpan.Zero);
            SourcePayload payload = new(
                "GBP",
                new Uri("https://example.test/gbp"),
                fixedTime,
                fixture,
                "test-hash");

            IngestionOptions options = new(
                new DateOnly(2026, 9, 14),
                new DateOnly(2026, 9, 15),
                ["GBP"],
                temporaryDirectory,
                new Uri("https://data-api.ecb.europa.eu/"),
                7);

            MarketDataPipeline pipeline = new(
                new StubDataSource([payload]),
                new FileDataStore(),
                new FixedTimeProvider(fixedTime));

            PipelineRunReport first = await pipeline.RunAsync(options, CancellationToken.None);
            PipelineRunReport second = await pipeline.RunAsync(options, CancellationToken.None);

            Assert.True(first.Quality.Passed);
            Assert.True(second.Quality.Passed);
            Assert.Equal(2, second.StoredObservationCount);

            string processedPath = Path.Combine(temporaryDirectory, "processed", "ecb_fx_rates.csv");
            CsvDocument stored = CsvDocument.Parse(await File.ReadAllTextAsync(processedPath));
            Assert.Equal(2, stored.Rows.Count);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class StubDataSource(IReadOnlyList<SourcePayload> payloads) : IEcbDataSource
    {
        public Task<IReadOnlyList<SourcePayload>> FetchAsync(
            IReadOnlyList<string> currencies,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken) => Task.FromResult(payloads);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
