using Dapper;
using Statements.WebAPI.Contracts.BankAccounts;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.BankAccounts;

/// <summary>
/// Handles bank account CRUD with Dapper, ensuring all operations are scoped to the owning user.
/// </summary>
public sealed class BankAccountService : IBankAccountService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly ILogger<BankAccountService> _logger;

    public BankAccountService(IDbExecutor dbExecutor, ILogger<BankAccountService> logger)
    {
        _dbExecutor = dbExecutor;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BankAccountResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing bank accounts for user {UserId}", userId);

        var accounts = await _dbExecutor.QueryAsync<BankAccountResponse>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    user_id AS UserId,
                    bank_name AS BankName,
                    account_name AS AccountName,
                    account_mask AS AccountMask,
                    currency AS Currency,
                    created_at AS CreatedAt,
                    updated_at AS UpdatedAt
                FROM bank_accounts
                WHERE user_id = @UserId
                ORDER BY created_at ASC
                """,
                new { UserId = userId },
                cancellationToken: cancellationToken));

        return accounts.AsList();
    }

    public async Task<BankAccountResponse> CreateAsync(Guid userId, CreateBankAccountRequest? request, CancellationToken cancellationToken)
    {
        var accountName = string.IsNullOrWhiteSpace(request?.AccountName) ? "Untitled" : request.AccountName.Trim();
        var bankName = string.IsNullOrWhiteSpace(request?.BankName) ? string.Empty : request.BankName.Trim();

        _logger.LogInformation("Creating bank account '{AccountName}' for user {UserId}", accountName, userId);

        var account = await _dbExecutor.QuerySingleAsync<BankAccountResponse>(
            new CommandDefinition(
                """
                INSERT INTO bank_accounts (user_id, bank_name, account_name, currency)
                VALUES (@UserId, @BankName, @AccountName, 'AUD')
                RETURNING
                    id AS Id,
                    user_id AS UserId,
                    bank_name AS BankName,
                    account_name AS AccountName,
                    account_mask AS AccountMask,
                    currency AS Currency,
                    created_at AS CreatedAt,
                    updated_at AS UpdatedAt
                """,
                new
                {
                    UserId = userId,
                    BankName = bankName,
                    AccountName = accountName
                },
                cancellationToken: cancellationToken));

        _logger.LogInformation("Bank account {AccountId} created for user {UserId}", account.Id, userId);
        return account;
    }

    public async Task<BankAccountResponse> UpdateAsync(Guid userId, Guid accountId, UpdateBankAccountRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating bank account {AccountId} for user {UserId}", accountId, userId);

        var account = await _dbExecutor.QuerySingleOrDefaultAsync<BankAccountResponse>(
            new CommandDefinition(
                """
                UPDATE bank_accounts
                SET
                    account_name = @AccountName,
                    bank_name = COALESCE(NULLIF(@BankName, ''), bank_name),
                    currency = COALESCE(NULLIF(@Currency, ''), currency),
                    updated_at = NOW()
                WHERE id = @Id AND user_id = @UserId
                RETURNING
                    id AS Id,
                    user_id AS UserId,
                    bank_name AS BankName,
                    account_name AS AccountName,
                    account_mask AS AccountMask,
                    currency AS Currency,
                    created_at AS CreatedAt,
                    updated_at AS UpdatedAt
                """,
                new
                {
                    Id = accountId,
                    UserId = userId,
                    request.AccountName,
                    request.BankName,
                    request.Currency
                },
                cancellationToken: cancellationToken));

        if (account is null)
        {
            _logger.LogWarning("Bank account {AccountId} not found for user {UserId}", accountId, userId);
            throw new InvalidOperationException("Bank account not found.");
        }

        _logger.LogInformation("Bank account {AccountId} updated for user {UserId}", accountId, userId);
        return account;
    }

    public async Task DeleteAsync(Guid userId, Guid accountId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting bank account {AccountId} for user {UserId}", accountId, userId);

        var rowsAffected = await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM bank_accounts
                WHERE id = @Id AND user_id = @UserId
                """,
                new { Id = accountId, UserId = userId },
                cancellationToken: cancellationToken));

        if (rowsAffected == 0)
        {
            _logger.LogWarning("Bank account {AccountId} not found for user {UserId}", accountId, userId);
            throw new InvalidOperationException("Bank account not found.");
        }

        _logger.LogInformation("Bank account {AccountId} deleted for user {UserId} (cascade removed statements and transactions)", accountId, userId);
    }
}
