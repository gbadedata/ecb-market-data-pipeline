using EcbMarketData.Domain;
using EcbMarketData.Pipeline;

namespace EcbMarketData.Tests;

public sealed class FxObservationMapperTests
{
    [Fact]
    public void Map_ConvertsTheEcbContractToCanonicalRates()
    {
        string content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ecb_fx_sample.csv"));
        DateTimeOffset fetchedAt = new(2026, 9, 16, 8, 30, 0, TimeSpan.Zero);
        SourcePayload payload = new(
            "GBP",
            new Uri("https://example.test/gbp"),
            fetchedAt,
            content,
            "test-hash");

        IReadOnlyList<FxObservation> observations = FxObservationMapper.Map(payload);

        Assert.Equal(2, observations.Count);
        Assert.All(observations, observation =>
        {
            Assert.Equal("EUR", observation.BaseCurrency);
            Assert.Equal("GBP", observation.QuoteCurrency);
            Assert.Equal(fetchedAt, observation.IngestedAtUtc);
        });
        Assert.Equal(0.8650m, observations[0].Rate);
        Assert.Equal(new DateOnly(2026, 9, 15), observations[1].ObservationDate);
    }
}
