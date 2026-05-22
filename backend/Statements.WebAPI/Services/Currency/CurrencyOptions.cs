namespace Statements.WebAPI.Services.Currency;

/// <summary>
/// Configuration options for currency conversion using an external exchange rate API.
/// </summary>
public sealed class CurrencyOptions
{
    public const string SectionName = "CurrencyApi";

    /// <summary>
    /// Base URL for the exchange rate API.
    /// </summary>
    public string BaseUrl { get; init; } = "https://open.er-api.com";

    /// <summary>
    /// API key (optional for free tiers, required for premium).
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// How long to cache exchange rates in minutes (default 60).
    /// </summary>
    public int CacheDurationMinutes { get; init; } = 60;
}
