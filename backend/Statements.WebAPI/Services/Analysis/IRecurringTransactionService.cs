using Statements.WebAPI.Contracts.Analysis;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Detects and manages recurring transaction patterns.
/// </summary>
public interface IRecurringTransactionService
{
    /// <summary>
    /// Scans recent transactions to detect recurring patterns and returns them.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="bankAccountId">Optional bank account filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of detected recurring transactions.</returns>
    Task<IReadOnlyList<RecurringTransactionResponse>> GetRecurringTransactionsAsync(
        Guid userId,
        Guid? bankAccountId,
        CancellationToken cancellationToken);
}
