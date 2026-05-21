using Dapper;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Manages monthly budgets per category, including upsert and progress calculation.
/// </summary>
public sealed class BudgetService : IBudgetService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly ILogger<BudgetService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BudgetService"/> class.
    /// </summary>
    /// <param name="dbExecutor">Executes Dapper database commands.</param>
    /// <param name="logger">Logger instance.</param>
    public BudgetService(IDbExecutor dbExecutor, ILogger<BudgetService> logger)
    {
        _dbExecutor = dbExecutor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BudgetResponse>> ListAsync(Guid userId, DateOnly monthYear, CancellationToken cancellationToken)
    {
        return (await _dbExecutor.QueryAsync<BudgetResponse>(
            new CommandDefinition(
                """
                SELECT
                    b.id AS Id,
                    b.category_id AS CategoryId,
                    c.name AS CategoryName,
                    b.month_year AS MonthYear,
                    b.amount AS Amount
                FROM budgets b
                JOIN transaction_categories c ON c.id = b.category_id
                WHERE b.user_id = @UserId AND b.month_year = @MonthYear
                ORDER BY c.name ASC
                """,
                new { UserId = userId, MonthYear = monthYear },
                cancellationToken: cancellationToken)))
            .AsList();
    }

    /// <inheritdoc />
    public async Task<BudgetResponse> CreateOrUpdateAsync(Guid userId, CreateBudgetRequest request, CancellationToken cancellationToken)
    {
        return await _dbExecutor.QuerySingleAsync<BudgetResponse>(
            new CommandDefinition(
                """
                INSERT INTO budgets (user_id, category_id, month_year, amount)
                VALUES (@UserId, @CategoryId, @MonthYear, @Amount)
                ON CONFLICT (user_id, category_id, month_year)
                DO UPDATE SET amount = @Amount, updated_at = NOW()
                RETURNING
                    id AS Id,
                    category_id AS CategoryId,
                    (SELECT name FROM transaction_categories WHERE id = @CategoryId) AS CategoryName,
                    month_year AS MonthYear,
                    amount AS Amount
                """,
                new
                {
                    UserId = userId,
                    CategoryId = request.CategoryId,
                    MonthYear = request.MonthYear,
                    Amount = request.Amount
                },
                cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, Guid budgetId, CancellationToken cancellationToken)
    {
        var rows = await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM budgets
                WHERE id = @Id AND user_id = @UserId
                """,
                new { Id = budgetId, UserId = userId },
                cancellationToken: cancellationToken));

        if (rows == 0)
        {
            throw new InvalidOperationException("Budget not found.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BudgetProgressResponse>> GetProgressAsync(
        Guid userId, DateOnly monthYear, Guid? bankAccountId, CancellationToken cancellationToken)
    {
        var monthStart = monthYear;
        var monthEnd = monthStart.AddMonths(1);

        return (await _dbExecutor.QueryAsync<BudgetProgressResponse>(
            new CommandDefinition(
                """
                SELECT
                    b.category_id AS CategoryId,
                    c.name AS CategoryName,
                    b.amount AS BudgetAmount,
                    COALESCE(SUM(t.amount), 0) AS ActualSpending
                FROM budgets b
                JOIN transaction_categories c ON c.id = b.category_id
                LEFT JOIN statement_transactions t
                    ON t.category_id = b.category_id
                    AND t.transaction_type = 'debit'
                    AND t.transaction_date >= @MonthStart
                    AND t.transaction_date < @MonthEnd
                LEFT JOIN bank_statements s ON s.id = t.bank_statement_id
                    AND s.user_id = @UserId
                    AND (@BankAccountId IS NULL OR t.bank_account_id = @BankAccountId)
                WHERE b.user_id = @UserId
                  AND b.month_year = @MonthYear
                GROUP BY b.category_id, c.name, b.amount
                ORDER BY c.name ASC
                """,
                new
                {
                    UserId = userId,
                    MonthYear = monthYear,
                    MonthStart = monthStart,
                    MonthEnd = monthEnd,
                    BankAccountId = bankAccountId
                },
                cancellationToken: cancellationToken)))
            .Select(p =>
            {
                var percentage = p.BudgetAmount > 0 ? (p.ActualSpending / p.BudgetAmount) * 100 : 0;
                var remaining = p.BudgetAmount - p.ActualSpending;
                return new BudgetProgressResponse
                {
                    CategoryId = p.CategoryId,
                    CategoryName = p.CategoryName,
                    BudgetAmount = p.BudgetAmount,
                    ActualSpending = p.ActualSpending,
                    PercentageUsed = Math.Round(percentage, 1),
                    Remaining = remaining,
                    IsOverBudget = remaining < 0
                };
            })
            .ToList();
    }
}
