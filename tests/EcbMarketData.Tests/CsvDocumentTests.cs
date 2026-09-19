using EcbMarketData.Infrastructure;

namespace EcbMarketData.Tests;

public sealed class CsvDocumentTests
{
    [Fact]
    public void Parse_HandlesQuotedCommasAndEscapedQuotes()
    {
        const string content = "name,description\r\nGBP,\"Rate, with \"\"quoted\"\" text\"\r\n";

        CsvDocument document = CsvDocument.Parse(content);

        CsvRow row = Assert.Single(document.Rows);
        Assert.Equal("GBP", row.Required("name"));
        Assert.Equal("Rate, with \"quoted\" text", row.Required("description"));
    }

    [Fact]
    public void Parse_RejectsRowsWithUnexpectedFieldCounts()
    {
        const string content = "a,b\n1,2,3\n";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => CsvDocument.Parse(content));

        Assert.Contains("expected 2", exception.Message, StringComparison.Ordinal);
    }
}
