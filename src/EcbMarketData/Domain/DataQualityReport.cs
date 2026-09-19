namespace EcbMarketData.Domain;

public enum QualitySeverity
{
    Warning,
    Error,
}

public sealed record QualityIssue(
    string Rule,
    QualitySeverity Severity,
    string Message,
    string? Currency = null,
    string? BusinessKey = null);

public sealed record CurrencyQualityMetric(
    string Currency,
    int ExpectedWeekdays,
    int ActualObservations,
    decimal WeekdayCoverageRatio,
    DateOnly? LatestObservationDate,
    int? StalenessDays);

public sealed record DataQualityReport(
    DateTimeOffset EvaluatedAtUtc,
    DateOnly RequestedStartDate,
    DateOnly RequestedEndDate,
    int ObservationCount,
    IReadOnlyList<CurrencyQualityMetric> CurrencyMetrics,
    IReadOnlyList<QualityIssue> Issues)
{
    public bool Passed => Issues.All(issue => issue.Severity != QualitySeverity.Error);
}

public sealed record PipelineRunReport(
    string RunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int SourceCount,
    int ExtractedObservationCount,
    int StoredObservationCount,
    string OutputDirectory,
    DataQualityReport Quality);

public sealed record RunManifest(
    string RunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    DateOnly RequestedStartDate,
    DateOnly RequestedEndDate,
    IReadOnlyList<string> RequestedCurrencies,
    int ExtractedObservationCount,
    int StoredObservationCount,
    IReadOnlyList<SourceManifestEntry> Sources);

public sealed record SourceManifestEntry(
    string Currency,
    string RequestUri,
    DateTimeOffset FetchedAtUtc,
    string Sha256,
    int Bytes);
