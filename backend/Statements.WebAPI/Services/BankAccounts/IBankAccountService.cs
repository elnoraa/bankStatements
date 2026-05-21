using Statements.WebAPI.Contracts.BankAccounts;

namespace Statements.WebAPI.Services.BankAccounts;

/// <summary>
/// Provides CRUD operations for user bank accounts.
/// </summary>
public interface IBankAccountService
{
    /// <summary>
    /// Lists all bank accounts belonging to the specified user.
    /// </summary>
    Task<IReadOnlyList<BankAccountResponse>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new bank account for the user.
    /// </summary>
    Task<BankAccountResponse> CreateAsync(Guid userId, CreateBankAccountRequest? request, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the name details of an existing bank account.
    /// </summary>
    Task<BankAccountResponse> UpdateAsync(Guid userId, Guid accountId, UpdateBankAccountRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a bank account and cascades to its statements and transactions.
    /// </summary>
    Task DeleteAsync(Guid userId, Guid accountId, CancellationToken cancellationToken);
}
