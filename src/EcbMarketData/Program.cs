using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using EcbMarketData.Configuration;
using EcbMarketData.Domain;
using EcbMarketData.Infrastructure;
using EcbMarketData.Pipeline;

namespace EcbMarketData;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            Console.WriteLine(IngestionOptions.Usage);
            return 0;
        }

        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            IngestionOptions options = IngestionOptions.Parse(args);

            using HttpClient httpClient = new()
            {
                Timeout = TimeSpan.FromSeconds(45),
            };
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("ecb-market-data-pipeline", "1.0"));

            MarketDataPipeline pipeline = new(
                new EcbApiClient(httpClient, options.ApiBaseUri),
                new FileDataStore());

            PipelineRunReport result = await pipeline
                .RunAsync(options, cancellation.Token)
                .ConfigureAwait(false);

            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return result.Quality.Passed ? 0 : 2;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(IngestionOptions.Usage);
            return 64;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Ingestion cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Ingestion failed: {exception.Message}");
            return 1;
        }
    }
}
