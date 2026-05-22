using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Statements.WebAPI.Services.Currency;

/// <summary>
/// Converts currencies using the open ER API (free, no key required for basic usage)
/// with in-memory caching to minimize external API calls.
/// </summary>
public sealed class CurrencyConverter : ICurrencyConverter
{
    private readonly HttpClient _httpClient;
    private readonly CurrencyOptions _options;
    private readonly ILogger<CurrencyConverter> _logger;
    private static readonly ConcurrentDictionary<string, (decimal Rate, DateTime FetchedAt)> _rateCache = new();
    private static readonly SemaphoreSlim _fetchLock = new(1, 1);

    public CurrencyConverter(
        HttpClient httpClient,
        IOptions<CurrencyOptions> options,
        ILogger<CurrencyConverter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<decimal> ConvertAsync(string fromCurrency, string toCurrency, decimal amount, CancellationToken cancellationToken)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
            return amount;

        var rate = await GetRateAsync(fromCurrency, toCurrency, cancellationToken);
        return Math.Round(amount * rate, 2);
    }

    /// <inheritdoc />
    public async Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken)
    {
        var cacheKey = $"{fromCurrency.ToUpperInvariant()}_{toCurrency.ToUpperInvariant()}";

        if (_rateCache.TryGetValue(cacheKey, out var cached))
        {
            var age = DateTime.UtcNow - cached.FetchedAt;
            if (age.TotalMinutes < _options.CacheDurationMinutes)
            {
                _logger.LogDebug("Using cached rate for {CacheKey}: {Rate}", cacheKey, cached.Rate);
                return cached.Rate;
            }
        }

        await _fetchLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_rateCache.TryGetValue(cacheKey, out var recheck))
            {
                var age = DateTime.UtcNow - recheck.FetchedAt;
                if (age.TotalMinutes < _options.CacheDurationMinutes)
                    return recheck.Rate;
            }

            _logger.LogInformation("Fetching exchange rate from {From} to {To}", fromCurrency, toCurrency);

            var url = $"{_options.BaseUrl.TrimEnd('/')}/v6/latest/{fromCurrency.ToUpperInvariant()}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("rates", out var rates))
            {
                _logger.LogWarning("Exchange rate API response missing 'rates' field");
                throw new InvalidOperationException("Unable to retrieve exchange rates.");
            }

            if (!rates.TryGetProperty(toCurrency.ToUpperInvariant(), out var rateElement))
            {
                _logger.LogWarning("Currency {To} not found in exchange rates", toCurrency);
                throw new InvalidOperationException($"Currency '{toCurrency}' is not supported.");
            }

            var rate = rateElement.GetDecimal();
            _rateCache[cacheKey] = (rate, DateTime.UtcNow);

            _logger.LogDebug("Fetched rate {From}/{To}: {Rate}", fromCurrency, toCurrency, rate);
            return rate;
        }
        finally
        {
            _fetchLock.Release();
        }
    }
}
