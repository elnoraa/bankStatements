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
}
