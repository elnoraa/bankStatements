using Dapper;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Detects recurring transactions by analyzing frequency patterns in statement transactions.
/// </summary>
public sealed class RecurringTransactionService : IRecurringTransactionService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly ILogger<RecurringTransactionService> _logger;

    public RecurringTransactionService(IDbExecutor dbExecutor, ILogger<RecurringTransactionService> logger)
    {
        _dbExecutor = dbExecutor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecurringTransactionResponse>> GetRecurringTransactionsAsync(
        Guid userId, Guid? bankAccountId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Detecting recurring transactions for user {UserId}, bankAccountId: {BankAccountId}",
            userId, bankAccountId);

        var transactions = await _dbExecutor.QueryAsync<RecurringTransactionResponse>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    description AS Description,
                    amount AS Amount,
                    frequency AS Frequency,
                    category_id AS CategoryId,
                    expected_day AS ExpectedDay,
                    last_detected AS LastDetected,
                    is_active AS IsActive
                FROM recurring_transactions
                WHERE user_id = @UserId
                AND (@BankAccountId IS NULL OR bank_account_id = @BankAccountId)
                AND is_active = TRUE
                ORDER BY last_detected DESC
                """,
                new { UserId = userId, BankAccountId = bankAccountId },
                cancellationToken: cancellationToken));

        _logger.LogInformation("Found {Count} recurring transactions for user {UserId}",
            transactions.Count(), userId);

        return transactions.AsList();
    }
}
