using Dapper;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Handles transaction editing with ownership verification through the
/// bank_statement ownership chain.
/// </summary>
public sealed class TransactionService : ITransactionService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(IDbExecutor dbExecutor, ILogger<TransactionService> logger)
    {
        _dbExecutor = dbExecutor;
        _logger = logger;
    }

    public async Task UpdateAsync(Guid userId, Guid transactionId, UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        var rows = await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE statement_transactions t
                SET
                    description = COALESCE(@Description, description),
                    amount = COALESCE(@Amount, amount),
                    category_id = @CategoryId
                FROM bank_statements s
                WHERE t.id = @TransactionId
                  AND t.bank_statement_id = s.id
                  AND s.user_id = @UserId
                """,
                new
                {
                    TransactionId = transactionId,
                    UserId = userId,
                    Description = request.Description,
                    Amount = request.Amount,
                    CategoryId = request.CategoryId
                },
                cancellationToken: cancellationToken));

        if (rows == 0)
        {
            _logger.LogWarning(
                "Transaction {TransactionId} not found or not owned by user {UserId}",
                transactionId, userId);
            throw new InvalidOperationException("Transaction not found.");
        }

        _logger.LogInformation(
            "Transaction {TransactionId} updated by user {UserId}",
            transactionId, userId);

        // Bulk apply category to all transactions with the same description
        if (request.ApplyToAll == true && request.CategoryId.HasValue && request.Description is not null)
        {
            var bulkRows = await _dbExecutor.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE statement_transactions t
                    SET category_id = @CategoryId
                    FROM bank_statements s
                    WHERE t.bank_statement_id = s.id
                      AND s.user_id = @UserId
                      AND LOWER(t.description) = LOWER(@Description)
                      AND t.id != @TransactionId
                    """,
                    new
                    {
                        CategoryId = request.CategoryId,
                        UserId = userId,
                        Description = request.Description,
                        TransactionId = transactionId
                    },
                    cancellationToken: cancellationToken));

            _logger.LogInformation(
                "Bulk category update applied to {Count} additional transactions for user {UserId}",
                bulkRows, userId);

            // Save the mapping as a persistent rule so future imports use this category
            await _dbExecutor.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO user_category_rules (user_id, description, category_id)
                    VALUES (@UserId, @Description, @CategoryId)
                    ON CONFLICT (user_id, description)
                    DO UPDATE SET category_id = @CategoryId, updated_at = NOW()
                    """,
                    new
                    {
                        UserId = userId,
                        Description = request.Description,
                        CategoryId = request.CategoryId
                    },
                    cancellationToken: cancellationToken));

            _logger.LogInformation(
                "Category rule saved for user {UserId}, description '{Description}'",
                userId, request.Description);
        }
    }

    public async Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        return (await _dbExecutor.QueryAsync<CategoryResponse>(
            new CommandDefinition(
                """
                SELECT id AS Id, name AS Name, transaction_type AS TransactionType
                FROM transaction_categories
                ORDER BY name ASC
                """,
                cancellationToken: cancellationToken)))
            .AsList();
    }
}
