namespace EcbMarketData.Domain;

public sealed record FxObservation(
    DateOnly ObservationDate,
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    string Status,
    string SourceSeries,
    DateTimeOffset IngestedAtUtc)
{
    public string BusinessKey => $"{ObservationDate:yyyy-MM-dd}|{BaseCurrency}|{QuoteCurrency}";
}

public sealed record SourcePayload(
    string Currency,
    Uri RequestUri,
    DateTimeOffset FetchedAtUtc,
    string Content,
    string Sha256);
