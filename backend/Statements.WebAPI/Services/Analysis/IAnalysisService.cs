using Statements.WebAPI.Contracts.Analysis;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Provides spending analysis and summary operations for user bank statements.
/// </summary>
public interface IAnalysisService
{
    /// <summary>
    /// Retrieves a spending summary for the specified user and optional filters.
    /// </summary>
    /// <param name="userId">The ID of the user to analyze.</param>
    /// <param name="bankAccountId">Optional bank account ID to filter by.</param>
    /// <param name="from">Optional start date for the analysis period.</param>
    /// <param name="to">Optional end date for the analysis period.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SpendingSummaryResponse"/> with aggregated spending data.</returns>
    Task<SpendingSummaryResponse> GetSummaryAsync(
        Guid userId,
        Guid? bankAccountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves spending trends aggregated by period (monthly/quarterly/yearly).
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="bankAccountId">Optional bank account ID to filter by.</param>
    /// <param name="period">The aggregation period: "monthly", "quarterly", or "yearly".</param>
    /// <param name="from">Optional start date.</param>
    /// <param name="to">Optional end date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SpendingTrendResponse"/> with trend data points.</returns>
    /// <summary>
    /// Retrieves a paginated list of transactions for the specified user and filters.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="bankAccountId">Optional bank account ID to filter by.</param>
    /// <param name="from">Optional start date.</param>
    /// <param name="to">Optional end date.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PaginatedTransactionsResponse"/> with the requested page.</returns>
    Task<PaginatedTransactionsResponse> GetTransactionsAsync(
        Guid userId,
        Guid? bankAccountId,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves spending trends aggregated by period (monthly/quarterly/yearly).
    /// </summary>
    Task<SpendingTrendResponse> GetSpendingTrendsAsync(
        Guid userId,
        Guid? bankAccountId,
        string period,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);
}
