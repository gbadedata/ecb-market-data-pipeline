using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using EcbMarketData.Domain;

namespace EcbMarketData.Infrastructure;

public interface IEcbDataSource
{
    Task<IReadOnlyList<SourcePayload>> FetchAsync(
        IReadOnlyList<string> currencies,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken);
}

public sealed class EcbApiClient : IEcbDataSource
{
    private const int MaximumAttempts = 4;
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public EcbApiClient(HttpClient httpClient, Uri baseUri)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
    }

    public async Task<IReadOnlyList<SourcePayload>> FetchAsync(
        IReadOnlyList<string> currencies,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currencies);

        Task<SourcePayload>[] requests = currencies
            .Select(currency => FetchCurrencyAsync(currency, startDate, endDate, cancellationToken))
            .ToArray();

        return await Task.WhenAll(requests).ConfigureAwait(false);
    }

    public Uri BuildRequestUri(string currency, DateOnly startDate, DateOnly endDate)
    {
        string normalizedCurrency = currency.ToUpperInvariant();
        string path = $"service/data/EXR/D.{Uri.EscapeDataString(normalizedCurrency)}.EUR.SP00.A";
        string query = string.Create(
            CultureInfo.InvariantCulture,
            $"?startPeriod={startDate:yyyy-MM-dd}&endPeriod={endDate:yyyy-MM-dd}&format=csvdata&detail=dataonly");

        return new Uri(_baseUri, path + query);
    }

    private async Task<SourcePayload> FetchCurrencyAsync(
        string currency,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        Uri requestUri = BuildRequestUri(currency, startDate, endDate);

        for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));

            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                DateTimeOffset fetchedAt = DateTimeOffset.UtcNow;
                return new SourcePayload(
                    currency.ToUpperInvariant(),
                    requestUri,
                    fetchedAt,
                    content,
                    ComputeSha256(content));
            }

            if (!ShouldRetry(response.StatusCode) || attempt == MaximumAttempts)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                string excerpt = body.Length <= 300 ? body : body[..300];
                throw new HttpRequestException(
                    $"ECB request for {currency} failed with {(int)response.StatusCode} " +
                    $"({response.ReasonPhrase}). Response: {excerpt}",
                    null,
                    response.StatusCode);
            }

            TimeSpan delay = GetRetryDelay(response, attempt);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The retry loop exited unexpectedly.");
    }

    private static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan serverDelay && serverDelay <= TimeSpan.FromSeconds(30))
        {
            return serverDelay;
        }

        return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
    }

    private static string ComputeSha256(string content)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
