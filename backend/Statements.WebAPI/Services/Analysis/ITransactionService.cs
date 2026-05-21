using Statements.WebAPI.Contracts.Analysis;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Provides operations for editing and managing parsed transactions.
/// </summary>
public interface ITransactionService
{
    /// <summary>
    /// Updates a transaction's editable fields. Only the owning user can update
    /// their transactions (verified via the bank_statement ownership chain).
    /// </summary>
    /// <param name="userId">The ID of the owning user.</param>
    /// <param name="transactionId">The transaction ID to update.</param>
    /// <param name="request">The fields to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(Guid userId, Guid transactionId, UpdateTransactionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all available transaction categories.
    /// </summary>
    Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(CancellationToken cancellationToken);
}
