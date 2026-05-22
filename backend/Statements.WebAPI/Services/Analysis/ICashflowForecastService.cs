using Statements.WebAPI.Contracts.Analysis;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Provides cash flow forecasting based on historical data and recurring transactions.
/// </summary>
public interface ICashflowForecastService
{
    /// <summary>
    /// Generates a cash flow forecast for the specified number of months.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="bankAccountId">Optional bank account filter.</param>
    /// <param name="months">Number of months to forecast (default 3, max 12).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CashflowForecastResponse"/> with projected monthly balances.</returns>
    Task<CashflowForecastResponse> GetForecastAsync(
        Guid userId,
        Guid? bankAccountId,
        int months,
        CancellationToken cancellationToken);
}
