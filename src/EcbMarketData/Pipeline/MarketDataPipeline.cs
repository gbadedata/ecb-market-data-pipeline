using EcbMarketData.Configuration;
using EcbMarketData.Domain;
using EcbMarketData.Infrastructure;

namespace EcbMarketData.Pipeline;

public sealed class MarketDataPipeline
{
    private readonly IEcbDataSource _dataSource;
    private readonly IMarketDataStore _dataStore;
    private readonly TimeProvider _timeProvider;

    public MarketDataPipeline(
        IEcbDataSource dataSource,
        IMarketDataStore dataStore,
        TimeProvider? timeProvider = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PipelineRunReport> RunAsync(
        IngestionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        string runId = $"{startedAt:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}"[..28];

        IReadOnlyList<SourcePayload> sources = await _dataSource
            .FetchAsync(options.Currencies, options.StartDate, options.EndDate, cancellationToken)
            .ConfigureAwait(false);

        FxObservation[] observations = sources
            .SelectMany(FxObservationMapper.Map)
            .OrderBy(observation => observation.ObservationDate)
            .ThenBy(observation => observation.QuoteCurrency, StringComparer.Ordinal)
            .ToArray();

        DateTimeOffset evaluatedAt = _timeProvider.GetUtcNow();
        DataQualityReport quality = DataQualityEvaluator.Evaluate(observations, options, evaluatedAt);
        DateTimeOffset completedAt = _timeProvider.GetUtcNow();

        int storedObservationCount = await _dataStore
            .PersistAsync(
                runId,
                startedAt,
                completedAt,
                options,
                sources,
                observations,
                quality,
                cancellationToken)
            .ConfigureAwait(false);

        return new PipelineRunReport(
            runId,
            startedAt,
            completedAt,
            sources.Count,
            observations.Length,
            storedObservationCount,
            options.OutputDirectory,
            quality);
    }
}
