using System.Globalization;
using EcbMarketData.Domain;
using EcbMarketData.Infrastructure;

namespace EcbMarketData.Pipeline;

public static class FxObservationMapper
{
    public static IReadOnlyList<FxObservation> Map(SourcePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CsvDocument document = CsvDocument.Parse(payload.Content);
        EnsureRequiredHeaders(document.Headers);

        List<FxObservation> observations = [];
        foreach (CsvRow row in document.Rows)
        {
            string quoteCurrency = row.Required("CURRENCY").ToUpperInvariant();
            string baseCurrency = row.Required("CURRENCY_DENOM").ToUpperInvariant();
            string dateText = row.Required("TIME_PERIOD");
            string rateText = row.Required("OBS_VALUE");

            if (!DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly observationDate))
            {
                throw new InvalidDataException(
                    $"CSV row {row.RowNumber} has invalid TIME_PERIOD '{dateText}'.");
            }

            if (!decimal.TryParse(
                    rateText,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal rate))
            {
                throw new InvalidDataException(
                    $"CSV row {row.RowNumber} has invalid OBS_VALUE '{rateText}'.");
            }

            string sourceSeries = row.Optional(
                "KEY",
                $"EXR.D.{quoteCurrency}.{baseCurrency}.SP00.A");

            observations.Add(new FxObservation(
                observationDate,
                baseCurrency,
                quoteCurrency,
                rate,
                row.Optional("OBS_STATUS", "A"),
                sourceSeries,
                payload.FetchedAtUtc));
        }

        return observations;
    }

    private static void EnsureRequiredHeaders(IReadOnlyList<string> headers)
    {
        string[] required = ["CURRENCY", "CURRENCY_DENOM", "TIME_PERIOD", "OBS_VALUE"];
        HashSet<string> available = new(headers, StringComparer.OrdinalIgnoreCase);
        string[] missing = required.Where(header => !available.Contains(header)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"ECB response does not match the expected contract. Missing: {string.Join(", ", missing)}.");
        }
    }
}
