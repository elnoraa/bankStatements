using Dapper;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Analysis;

public sealed class AnalysisService : IAnalysisService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AnalysisService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SpendingSummaryResponse> GetSummaryAsync(
        Guid userId,
        Guid? bankAccountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var parameters = new
        {
            UserId = userId,
            BankAccountId = bankAccountId,
            From = from,
            To = to
        };

        var totals = await connection.QuerySingleAsync<CashflowTotals>(
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
                AND (@From IS NULL OR t.transaction_date >= @From)
                AND (@To IS NULL OR t.transaction_date <= @To)
                """,
                parameters,
                cancellationToken: cancellationToken));

        var spendingByCategory = (await connection.QueryAsync<CategorySpendingResponse>(
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
                AND (@From IS NULL OR t.transaction_date >= @From)
                AND (@To IS NULL OR t.transaction_date <= @To)
                GROUP BY COALESCE(c.name, 'Uncategorised')
                ORDER BY TotalDebit DESC
                """,
                parameters,
                cancellationToken: cancellationToken))).AsList();

        var recentTransactions = (await connection.QueryAsync<RecentTransactionResponse>(
            new CommandDefinition(
                """
                SELECT
                    t.id AS Id,
                    t.transaction_date AS TransactionDate,
                    t.description AS Description,
                    t.amount AS Amount,
                    t.transaction_type AS TransactionType,
                    c.name AS Category
                FROM statement_transactions t
                JOIN bank_statements s ON s.id = t.bank_statement_id
                LEFT JOIN transaction_categories c ON c.id = t.category_id
                WHERE s.user_id = @UserId
                AND (@BankAccountId IS NULL OR t.bank_account_id = @BankAccountId)
                AND (@From IS NULL OR t.transaction_date >= @From)
                AND (@To IS NULL OR t.transaction_date <= @To)
                ORDER BY t.transaction_date DESC, t.created_at DESC
                LIMIT 20
                """,
                parameters,
                cancellationToken: cancellationToken))).AsList();

        var netCashflow = totals.TotalCredit - totals.TotalDebit;

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

    private sealed record CashflowTotals(
        DateOnly? PeriodStart,
        DateOnly? PeriodEnd,
        decimal TotalCredit,
        decimal TotalDebit);
}
