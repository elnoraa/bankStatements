using Dapper;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Provides spending analysis by querying statement transactions and aggregating results.
/// </summary>
public sealed class AnalysisService : IAnalysisService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly ILogger<AnalysisService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisService"/> class.
    /// </summary>
    /// <param name="dbExecutor">Executes Dapper database commands.</param>
    /// <param name="logger">Logger instance.</param>
    public AnalysisService(IDbExecutor dbExecutor, ILogger<AnalysisService> logger)
    {
        _dbExecutor = dbExecutor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SpendingSummaryResponse> GetSummaryAsync(
        Guid userId,
        Guid? bankAccountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting spending summary for user {UserId}, bankAccountId: {BankAccountId}, from: {From}, to: {To}",
            userId, bankAccountId, from, to);

        var parameters = new
        {
            UserId = userId,
            BankAccountId = bankAccountId,
            From = from,
            To = to
        };

        var totals = await _dbExecutor.QuerySingleAsync<CashflowTotals>(
            new CommandDefinition(
                """
                SELECT
                    MIN(t.transaction_date) AS PeriodStart,
                    MAX(t.transaction_date) AS PeriodEnd,
                    COALESCE(SUM(CASE WHEN t.transaction_type = 'credit' THEN t.amount ELSE 0 END), 0) AS TotalCredit,
                    COALESCE(SUM(CASE WHEN t.transaction_type = 'debit' THEN t.amount ELSE 0 END), 0) AS TotalDebit
                FROM statement_transactions t
                JOIN bank_statements s ON s.id = t.bank_statement_id
                WHERE s.user_id = @UserId
                AND (@BankAccountId IS NULL OR t.bank_account_id = @BankAccountId)
                AND (@From::date IS NULL OR t.transaction_date >= @From::date)
                AND (@To::date IS NULL OR t.transaction_date <= @To::date)
                """,
                parameters,
                cancellationToken: cancellationToken));

        var spendingByCategory = (await _dbExecutor.QueryAsync<CategorySpendingResponse>(
            new CommandDefinition(
                """
                SELECT
                    COALESCE(c.name, 'Uncategorised') AS Category,
                    COALESCE(SUM(t.amount), 0) AS TotalDebit,
                    COUNT(*)::int AS TransactionCount
                FROM statement_transactions t
                JOIN bank_statements s ON s.id = t.bank_statement_id
                LEFT JOIN transaction_categories c ON c.id = t.category_id
                WHERE s.user_id = @UserId
                AND t.transaction_type = 'debit'
                AND (@BankAccountId IS NULL OR t.bank_account_id = @BankAccountId)
                AND (@From::date IS NULL OR t.transaction_date >= @From::date)
                AND (@To::date IS NULL OR t.transaction_date <= @To::date)
                GROUP BY COALESCE(c.name, 'Uncategorised')
                ORDER BY TotalDebit DESC
                """,
                parameters,
                cancellationToken: cancellationToken))).AsList();

        var recentTransactions = (await _dbExecutor.QueryAsync<RecentTransactionResponse>(
            new CommandDefinition(
                """
                SELECT
                    t.id AS Id,
                    t.transaction_date AS TransactionDate,
                    t.description AS Description,
                    t.amount AS Amount,
                    t.transaction_type AS TransactionType,
                    c.name AS Category,
                    c.id AS CategoryId
                FROM statement_transactions t
                JOIN bank_statements s ON s.id = t.bank_statement_id
                LEFT JOIN transaction_categories c ON c.id = t.category_id
                WHERE s.user_id = @UserId
                AND (@BankAccountId IS NULL OR t.bank_account_id = @BankAccountId)
                AND (@From::date IS NULL OR t.transaction_date >= @From::date)
                AND (@To::date IS NULL OR t.transaction_date <= @To::date)
                ORDER BY t.transaction_date DESC, t.created_at DESC
                LIMIT 20
                """,
                parameters,
                cancellationToken: cancellationToken))).AsList();

        var netCashflow = totals.TotalCredit - totals.TotalDebit;

        _logger.LogInformation(
            "Spending summary for user {UserId}: {TransactionCount} transactions, {CategoryCount} categories, net cashflow: {NetCashflow}",
            userId, recentTransactions.Count, spendingByCategory.Count, netCashflow);

        return new SpendingSummaryResponse(
            totals.PeriodStart,
            totals.PeriodEnd,
            totals.TotalCredit,
            totals.TotalDebit,
            netCashflow,
            netCashflow >= 0,
            spendingByCategory,
            recentTransactions);
    }

    internal sealed class CashflowTotals
    {
        public DateOnly? PeriodStart { get; init; }
        public DateOnly? PeriodEnd { get; init; }
        public decimal TotalCredit { get; init; }
        public decimal TotalDebit { get; init; }
    }
}
