using EcbMarketData.Configuration;
using EcbMarketData.Domain;

namespace EcbMarketData.Pipeline;

public static class DataQualityEvaluator
{
    public static DataQualityReport Evaluate(
        IReadOnlyList<FxObservation> observations,
        IngestionOptions options,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(options);

        List<QualityIssue> issues = [];
        HashSet<string> requestedCurrencies = new(options.Currencies, StringComparer.Ordinal);

        foreach (FxObservation observation in observations)
        {
            if (observation.BaseCurrency != "EUR")
            {
                issues.Add(new QualityIssue(
                    "base_currency",
                    QualitySeverity.Error,
                    $"Expected EUR base currency but found {observation.BaseCurrency}.",
                    observation.QuoteCurrency,
                    observation.BusinessKey));
            }

            if (!requestedCurrencies.Contains(observation.QuoteCurrency))
            {
                issues.Add(new QualityIssue(
                    "requested_currency",
                    QualitySeverity.Error,
                    "Response contains a currency that was not requested.",
                    observation.QuoteCurrency,
                    observation.BusinessKey));
            }

            if (observation.Rate <= 0)
            {
                issues.Add(new QualityIssue(
                    "positive_rate",
                    QualitySeverity.Error,
                    $"Exchange rate must be positive; found {observation.Rate}.",
                    observation.QuoteCurrency,
                    observation.BusinessKey));
            }

            if (observation.ObservationDate < options.StartDate || observation.ObservationDate > options.EndDate)
            {
                issues.Add(new QualityIssue(
                    "requested_date_range",
                    QualitySeverity.Error,
                    "Observation date falls outside the requested period.",
                    observation.QuoteCurrency,
                    observation.BusinessKey));
            }
        }

        foreach (IGrouping<string, FxObservation> duplicate in observations
                     .GroupBy(observation => observation.BusinessKey, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            FxObservation first = duplicate.First();
            issues.Add(new QualityIssue(
                "unique_business_key",
                QualitySeverity.Error,
                $"Found {duplicate.Count()} observations for the same date and currency.",
                first.QuoteCurrency,
                duplicate.Key));
        }

        int expectedWeekdays = CountWeekdays(options.StartDate, options.EndDate);
        List<CurrencyQualityMetric> metrics = [];
        foreach (string currency in options.Currencies)
        {
            FxObservation[] currencyRows = observations
                .Where(observation => observation.QuoteCurrency == currency)
                .OrderBy(observation => observation.ObservationDate)
                .ToArray();

            DateOnly? latest = currencyRows.Length == 0 ? null : currencyRows[^1].ObservationDate;
            int? staleness = latest.HasValue ? options.EndDate.DayNumber - latest.Value.DayNumber : null;
            int actual = currencyRows.Select(row => row.ObservationDate).Distinct().Count();
            decimal coverage = expectedWeekdays == 0
                ? 1m
                : Math.Round((decimal)actual / expectedWeekdays, 4, MidpointRounding.AwayFromZero);

            metrics.Add(new CurrencyQualityMetric(
                currency,
                expectedWeekdays,
                actual,
                coverage,
                latest,
                staleness));

            if (currencyRows.Length == 0)
            {
                issues.Add(new QualityIssue(
                    "currency_present",
                    QualitySeverity.Error,
                    "No observations were returned for the requested currency.",
                    currency));
                continue;
            }

            if (staleness.HasValue && staleness.Value > options.MaxStalenessDays)
            {
                issues.Add(new QualityIssue(
                    "freshness",
                    QualitySeverity.Error,
                    $"Latest observation is {staleness} days behind the requested end date; " +
                    $"maximum allowed is {options.MaxStalenessDays}.",
                    currency));
            }

            if (coverage < 0.90m)
            {
                issues.Add(new QualityIssue(
                    "weekday_coverage",
                    QualitySeverity.Warning,
                    $"Weekday coverage is {coverage:P1}. Holidays may explain some gaps.",
                    currency));
            }
        }

        return new DataQualityReport(
            evaluatedAtUtc,
            options.StartDate,
            options.EndDate,
            observations.Count,
            metrics,
            issues);
    }

    private static int CountWeekdays(DateOnly startDate, DateOnly endDate)
    {
        int count = 0;
        for (DateOnly date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                count++;
            }
        }

        return count;
    }
}
