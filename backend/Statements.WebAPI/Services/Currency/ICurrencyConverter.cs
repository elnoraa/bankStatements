namespace Statements.WebAPI.Services.Currency;

/// <summary>
/// Provides currency conversion using exchange rate APIs with caching.
/// </summary>
public interface ICurrencyConverter
{
    /// <summary>
    /// Converts an amount from one currency to another.
    /// </summary>
    /// <param name="fromCurrency">Source currency code (e.g., "USD").</param>
    /// <param name="toCurrency">Target currency code (e.g., "AUD").</param>
    /// <param name="amount">Amount to convert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The converted amount.</returns>
    Task<decimal> ConvertAsync(string fromCurrency, string toCurrency, decimal amount, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the exchange rate between two currencies.
    /// </summary>
    /// <param name="fromCurrency">Source currency code.</param>
    /// <param name="toCurrency">Target currency code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exchange rate (1 fromCurrency = X toCurrency).</returns>
    Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken);
}
