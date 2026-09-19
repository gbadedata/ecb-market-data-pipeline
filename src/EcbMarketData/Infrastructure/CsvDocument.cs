using System.Collections.ObjectModel;
using System.Text;

namespace EcbMarketData.Infrastructure;

public sealed class CsvDocument
{
    private CsvDocument(IReadOnlyList<string> headers, IReadOnlyList<CsvRow> rows)
    {
        Headers = headers;
        Rows = rows;
    }

    public IReadOnlyList<string> Headers { get; }

    public IReadOnlyList<CsvRow> Rows { get; }

    public static CsvDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        List<IReadOnlyList<string>> records = ParseRecords(content);
        if (records.Count == 0)
        {
            throw new InvalidDataException("CSV input is empty.");
        }

        string[] headers = records[0]
            .Select((header, index) => index == 0 ? header.TrimStart('\uFEFF').Trim() : header.Trim())
            .ToArray();

        if (headers.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("CSV contains an empty header name.");
        }

        string? duplicateHeader = headers
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateHeader is not null)
        {
            throw new InvalidDataException($"CSV contains duplicate header '{duplicateHeader}'.");
        }

        List<CsvRow> rows = [];
        for (int index = 1; index < records.Count; index++)
        {
            IReadOnlyList<string> record = records[index];
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
            {
                continue;
            }

            if (record.Count != headers.Length)
            {
                throw new InvalidDataException(
                    $"CSV row {index + 1} has {record.Count} fields; expected {headers.Length}.");
            }

            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            for (int column = 0; column < headers.Length; column++)
            {
                values.Add(headers[column], record[column]);
            }

            rows.Add(new CsvRow(index + 1, new ReadOnlyDictionary<string, string>(values)));
        }

        return new CsvDocument(headers, rows);
    }

    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        bool requiresQuotes = value.Contains(',') || value.Contains('"') ||
                              value.Contains('\r') || value.Contains('\n');
        return requiresQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static List<IReadOnlyList<string>> ParseRecords(string content)
    {
        List<IReadOnlyList<string>> records = [];
        List<string> fields = [];
        StringBuilder field = new();
        bool insideQuotes = false;

        for (int index = 0; index < content.Length; index++)
        {
            char character = content[index];
            if (insideQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        insideQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    insideQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    AddRecord(records, fields, field);
                    if (index + 1 < content.Length && content[index + 1] == '\n')
                    {
                        index++;
                    }

                    break;
                case '\n':
                    AddRecord(records, fields, field);
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (insideQuotes)
        {
            throw new InvalidDataException("CSV input ends inside a quoted field.");
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            AddRecord(records, fields, field);
        }

        return records;
    }

    private static void AddRecord(
        List<IReadOnlyList<string>> records,
        List<string> fields,
        StringBuilder field)
    {
        fields.Add(field.ToString());
        records.Add(fields.ToArray());
        fields.Clear();
        field.Clear();
    }
}

public sealed record CsvRow(int RowNumber, IReadOnlyDictionary<string, string> Values)
{
    public string Required(string column)
    {
        if (!Values.TryGetValue(column, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"CSV row {RowNumber} is missing required column '{column}'.");
        }

        return value.Trim();
    }

    public string Optional(string column, string defaultValue = "") =>
        Values.TryGetValue(column, out string? value) ? value.Trim() : defaultValue;
}
