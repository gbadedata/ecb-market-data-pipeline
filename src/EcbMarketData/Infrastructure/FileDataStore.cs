using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EcbMarketData.Configuration;
using EcbMarketData.Domain;

namespace EcbMarketData.Infrastructure;

public interface IMarketDataStore
{
    Task<int> PersistAsync(
        string runId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        IngestionOptions options,
        IReadOnlyList<SourcePayload> sources,
        IReadOnlyList<FxObservation> observations,
        DataQualityReport quality,
        CancellationToken cancellationToken);
}

public sealed class FileDataStore : IMarketDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task<int> PersistAsync(
        string runId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        IngestionOptions options,
        IReadOnlyList<SourcePayload> sources,
        IReadOnlyList<FxObservation> observations,
        DataQualityReport quality,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(quality);

        string rawDirectory = Path.Combine(options.OutputDirectory, "raw", runId);
        string processedDirectory = Path.Combine(options.OutputDirectory, "processed");
        string reportsDirectory = Path.Combine(options.OutputDirectory, "reports");
        Directory.CreateDirectory(rawDirectory);
        Directory.CreateDirectory(processedDirectory);
        Directory.CreateDirectory(reportsDirectory);

        foreach (SourcePayload source in sources)
        {
            string rawPath = Path.Combine(rawDirectory, $"{source.Currency.ToLowerInvariant()}.csv");
            await AtomicWriteAsync(rawPath, source.Content, cancellationToken).ConfigureAwait(false);
        }

        int storedObservationCount = 0;
        if (quality.Passed)
        {
            string processedPath = Path.Combine(processedDirectory, "ecb_fx_rates.csv");
            IReadOnlyList<FxObservation> existing = await LoadExistingAsync(
                    processedPath,
                    cancellationToken)
                .ConfigureAwait(false);

            FxObservation[] merged = existing
                .Concat(observations)
                .GroupBy(observation => observation.BusinessKey, StringComparer.Ordinal)
                .Select(group => group.Last())
                .OrderBy(observation => observation.ObservationDate)
                .ThenBy(observation => observation.QuoteCurrency, StringComparer.Ordinal)
                .ToArray();

            await AtomicWriteAsync(
                    processedPath,
                    SerializeObservations(merged),
                    cancellationToken)
                .ConfigureAwait(false);
            storedObservationCount = merged.Length;
        }

        RunManifest manifest = new(
            runId,
            startedAtUtc,
            completedAtUtc,
            options.StartDate,
            options.EndDate,
            options.Currencies,
            observations.Count,
            storedObservationCount,
            sources.Select(source => new SourceManifestEntry(
                    source.Currency,
                    source.RequestUri.ToString(),
                    source.FetchedAtUtc,
                    source.Sha256,
                    Encoding.UTF8.GetByteCount(source.Content)))
                .ToArray());

        string qualityJson = JsonSerializer.Serialize(quality, JsonOptions) + Environment.NewLine;
        string manifestJson = JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine;

        await AtomicWriteAsync(
                Path.Combine(reportsDirectory, $"{runId}-quality-report.json"),
                qualityJson,
                cancellationToken)
            .ConfigureAwait(false);
        await AtomicWriteAsync(
                Path.Combine(reportsDirectory, "latest-quality-report.json"),
                qualityJson,
                cancellationToken)
            .ConfigureAwait(false);
        await AtomicWriteAsync(
                Path.Combine(reportsDirectory, $"{runId}-manifest.json"),
                manifestJson,
                cancellationToken)
            .ConfigureAwait(false);
        await AtomicWriteAsync(
                Path.Combine(reportsDirectory, "latest-manifest.json"),
                manifestJson,
                cancellationToken)
            .ConfigureAwait(false);

        return storedObservationCount;
    }

    private static async Task<IReadOnlyList<FxObservation>> LoadExistingAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        CsvDocument document = CsvDocument.Parse(content);
        List<FxObservation> observations = [];

        foreach (CsvRow row in document.Rows)
        {
            DateOnly date = DateOnly.ParseExact(
                row.Required("observation_date"),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            decimal rate = decimal.Parse(
                row.Required("rate"),
                NumberStyles.Number,
                CultureInfo.InvariantCulture);
            DateTimeOffset ingestedAt = DateTimeOffset.Parse(
                row.Required("ingested_at_utc"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);

            observations.Add(new FxObservation(
                date,
                row.Required("base_currency"),
                row.Required("quote_currency"),
                rate,
                row.Required("status"),
                row.Required("source_series"),
                ingestedAt));
        }

        return observations;
    }

    private static string SerializeObservations(IEnumerable<FxObservation> observations)
    {
        StringBuilder builder = new();
        builder.AppendLine(
            "observation_date,base_currency,quote_currency,rate,status,source_series,ingested_at_utc");

        foreach (FxObservation observation in observations)
        {
            string[] fields =
            [
                observation.ObservationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                observation.BaseCurrency,
                observation.QuoteCurrency,
                observation.Rate.ToString("G29", CultureInfo.InvariantCulture),
                observation.Status,
                observation.SourceSeries,
                observation.IngestedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ];

            builder.AppendLine(string.Join(',', fields.Select(CsvDocument.Escape)));
        }

        return builder.ToString();
    }

    private static async Task AtomicWriteAsync(
        string destinationPath,
        string content,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException($"No directory was found for '{destinationPath}'.");
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                    temporaryPath,
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
