using System.Globalization;

namespace EcbMarketData.Configuration;

public sealed record IngestionOptions(
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<string> Currencies,
    string OutputDirectory,
    Uri ApiBaseUri,
    int MaxStalenessDays)
{
    private static readonly string[] DefaultCurrencies = ["GBP", "USD", "JPY", "CHF"];

    public static IngestionOptions Parse(string[] args, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        DateOnly effectiveToday = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        Dictionary<string, string> values = ParseArguments(args);

        DateOnly startDate = ParseDate(values, "--start", effectiveToday.AddDays(-30));
        DateOnly endDate = ParseDate(values, "--end", effectiveToday);
        if (startDate > endDate)
        {
            throw new ArgumentException("--start must be on or before --end.");
        }

        string[] currencies = values.TryGetValue("--currencies", out string? currencyText)
            ? currencyText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : DefaultCurrencies;

        currencies = currencies
            .Select(currency => currency.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (currencies.Length == 0 || currencies.Any(currency =>
                currency.Length != 3 || currency.Any(character => character is < 'A' or > 'Z')))
        {
            throw new ArgumentException("--currencies must contain comma-separated ISO 4217 codes such as GBP,USD.");
        }

        string outputDirectory = values.GetValueOrDefault("--output", "data/output");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("--output cannot be empty.");
        }

        string baseUrl = values.GetValueOrDefault("--base-url", "https://data-api.ecb.europa.eu/");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? apiBaseUri) ||
            (apiBaseUri.Scheme != Uri.UriSchemeHttps && apiBaseUri.Host != "localhost"))
        {
            throw new ArgumentException("--base-url must be an absolute HTTPS URL, or localhost for testing.");
        }

        int maxStalenessDays = ParsePositiveInteger(values, "--max-staleness-days", 7);

        return new IngestionOptions(
            startDate,
            endDate,
            currencies,
            Path.GetFullPath(outputDirectory),
            apiBaseUri,
            maxStalenessDays);
    }

    public static string Usage =>
        "Usage: dotnet run --project src/EcbMarketData -- " +
        "[--start YYYY-MM-DD] [--end YYYY-MM-DD] " +
        "[--currencies GBP,USD,JPY,CHF] [--output data/output] " +
        "[--max-staleness-days 7]";

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        HashSet<string> supported =
        [
            "--start",
            "--end",
            "--currencies",
            "--output",
            "--base-url",
            "--max-staleness-days",
        ];

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for '{args[index]}'.");
            }

            string key = args[index];
            if (!supported.Contains(key))
            {
                throw new ArgumentException($"Unknown option '{key}'. {Usage}");
            }

            if (!values.TryAdd(key, args[index + 1]))
            {
                throw new ArgumentException($"Option '{key}' was supplied more than once.");
            }
        }

        return values;
    }

    private static DateOnly ParseDate(
        Dictionary<string, string> values,
        string key,
        DateOnly defaultValue)
    {
        if (!values.TryGetValue(key, out string? value))
        {
            return defaultValue;
        }

        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed))
        {
            throw new ArgumentException($"{key} must use YYYY-MM-DD format.");
        }

        return parsed;
    }

    private static int ParsePositiveInteger(
        Dictionary<string, string> values,
        string key,
        int defaultValue)
    {
        if (!values.TryGetValue(key, out string? value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
        {
            throw new ArgumentException($"{key} must be a positive integer.");
        }

        return parsed;
    }
}
