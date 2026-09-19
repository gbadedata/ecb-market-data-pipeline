using EcbMarketData.Infrastructure;

namespace EcbMarketData.Tests;

public sealed class EcbApiClientTests
{
    [Fact]
    public void BuildRequestUri_UsesTheDocumentedExchangeRateSeriesKey()
    {
        using HttpClient httpClient = new();
        EcbApiClient client = new(httpClient, new Uri("https://data-api.ecb.europa.eu/"));

        Uri uri = client.BuildRequestUri(
            "gbp",
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 15));

        Assert.Equal(
            "https://data-api.ecb.europa.eu/service/data/EXR/D.GBP.EUR.SP00.A" +
            "?startPeriod=2026-09-01&endPeriod=2026-09-15&format=csvdata&detail=dataonly",
            uri.ToString());
    }
}
