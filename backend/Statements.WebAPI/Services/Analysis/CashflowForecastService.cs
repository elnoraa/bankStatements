using Dapper;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Forecasts future cash flow using historical averages and recurring transaction data.
/// </summary>
public sealed class CashflowForecastService : ICashflowForecastService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly ILogger<CashflowForecastService> _logger;

    public CashflowForecastService(IDbExecutor dbExecutor, ILogger<CashflowForecastService> logger)
    {
        _dbExecutor = dbExecutor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CashflowForecastResponse> GetForecastAsync(
        Guid userId, Guid? bankAccountId, int months, CancellationToken cancellationToken)
    {
        months = Math.Clamp(months, 1, 12);
        _logger.LogInformation("Generating cash flow forecast for user {UserId}: {Months} months", userId, months);

        // Get average monthly credit and debit over the last 6 months
        var averages = await _dbExecutor.QuerySingleOrDefaultAsync<MonthlyAverages>(
            new CommandDefinition(
                """
                SELECT
                    COALESCE(AVG(CASE WHEN t.transaction_type = 'credit' THEN t.amount ELSE 0 END), 0) AS AvgCredit,
                    COALESCE(AVG(CASE WHEN t.transaction_type = 'debit' THEN t.amount ELSE 0 END), 0) AS AvgDebit
                FROM statement_transactions t
                JOIN bank_statements s ON s.id = t.bank_statement_id
                WHERE s.user_id = @UserId
                AND t.transaction_date >= NOW() - INTERVAL '6 months'
                AND (@BankAccountId IS NULL OR t.bank_account_id = @BankAccountId)
                """,
                new { UserId = userId, BankAccountId = bankAccountId },
                cancellationToken: cancellationToken));

        // Get recurring transactions for more accurate projections
        var recurring = await _dbExecutor.QueryAsync<dynamic>(
            new CommandDefinition(
                """
                SELECT amount, frequency, expected_day
                FROM recurring_transactions
                WHERE user_id = @UserId
                AND (@BankAccountId IS NULL OR bank_account_id = @BankAccountId)
                AND is_active = TRUE
                """,
                new { UserId = userId, BankAccountId = bankAccountId },
                cancellationToken: cancellationToken));

        var avgCredit = averages?.AvgCredit ?? 0;
        var avgDebit = averages?.AvgDebit ?? 0;

        var forecastMonths = new List<ForecastMonth>(months);
        var totalProjectedCredit = 0m;
        var totalProjectedDebit = 0m;

        for (int i = 0; i < months; i++)
        {
            var monthDate = DateTime.UtcNow.AddMonths(i);
            var monthLabel = $"{monthDate:yyyy-MM}";

            var recurringCredit = 0m;
            var recurringDebit = 0m;

            foreach (var r in recurring)
            {
                var amount = (decimal)r.amount;
                var frequency = (string)r.frequency;

                var occurrences = frequency switch
                {
                    "weekly" => 4.33m,
                    "fortnightly" => 2.17m,
                    "monthly" => 1m,
                    "quarterly" => 0.33m,
                    "yearly" => 0.08m,
                    _ => 0m
                };

                if (amount >= 0)
                    recurringCredit += amount * occurrences;
                else
                    recurringDebit += Math.Abs(amount) * occurrences;
            }

            var projectedCredit = Math.Max(avgCredit, recurringCredit);
            var projectedDebit = Math.Max(avgDebit, recurringDebit);
            var projectedBalance = projectedCredit - projectedDebit;
            var confidence = recurring.Any() ? "high" : "low";

            forecastMonths.Add(new ForecastMonth(
                monthLabel,
                projectedCredit,
                projectedDebit,
                projectedBalance,
                confidence));

            totalProjectedCredit += projectedCredit;
            totalProjectedDebit += projectedDebit;
        }

        return new CashflowForecastResponse(
            forecastMonths.AsReadOnly(),
            totalProjectedCredit,
            totalProjectedDebit,
            avgCredit,
            avgDebit);
    }

    internal sealed class MonthlyAverages
    {
        public decimal AvgCredit { get; init; }
        public decimal AvgDebit { get; init; }
    }
}
