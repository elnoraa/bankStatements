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

    /// <inheritdoc />
    public async Task<PaginatedTransactionsResponse> GetTransactionsAsync(
        Guid userId,
        Guid? bankAccountId,
        DateOnly? from,
        DateOnly? to,
        string? search,
        Guid? categoryId,
        decimal? minAmount,
        decimal? maxAmount,
        string? transactionType,
        string? sortBy,
        string? sortOrder,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Getting transactions for user {UserId}, page {Page}, pageSize {PageSize}, " +
            "search: {Search}, categoryId: {CategoryId}, minAmount: {MinAmount}, maxAmount: {MaxAmount}, type: {TransactionType}, sortBy: {SortBy}, sortOrder: {SortOrder}",
            userId, page, pageSize, search, categoryId, minAmount, maxAmount, transactionType, sortBy, sortOrder);

        var offset = (page - 1) * pageSize;

        var countParams = new {
            UserId = userId,
            BankAccountId = bankAccountId,
            From = from,
            To = to,
            Search = search,
            CategoryId = categoryId,
            MinAmount = minAmount,
            MaxAmount = maxAmount,
            TransactionType = transactionType
        };

        var totalCount = await _dbExecutor.QuerySingleAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)::int
                FROM statement_transactions t
                JOIN bank_statements s ON s.id = t.bank_statement_id
                WHERE s.user_id = @UserId
                AND (@BankAccountId IS NULL OR t.bank_account_id = @BankAccountId)
                AND (@From::date IS NULL OR t.transaction_date >= @From::date)
                AND (@To::date IS NULL OR t.transaction_date <= @To::date)
                AND (@Search IS NULL OR t.description ILIKE '%' || @Search || '%')
                AND (@CategoryId IS NULL OR t.category_id = @CategoryId)
                AND (@MinAmount IS NULL OR t.amount >= @MinAmount)
                AND (@MaxAmount IS NULL OR t.amount <= @MaxAmount)
                AND (@TransactionType IS NULL OR t.transaction_type = @TransactionType)
                """,
                countParams,
                cancellationToken: cancellationToken));

        // Build ORDER BY clause with whitelist to prevent SQL injection
        var orderBy = BuildOrderByClause(sortBy, sortOrder);

        var items = (await _dbExecutor.QueryAsync<RecentTransactionResponse>(
            new CommandDefinition(
                $"""
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
                AND (@Search IS NULL OR t.description ILIKE '%' || @Search || '%')
                AND (@CategoryId IS NULL OR t.category_id = @CategoryId)
                AND (@MinAmount IS NULL OR t.amount >= @MinAmount)
                AND (@MaxAmount IS NULL OR t.amount <= @MaxAmount)
                AND (@TransactionType IS NULL OR t.transaction_type = @TransactionType)
                ORDER BY {orderBy}
                LIMIT @PageSize OFFSET @Offset
                """,
                new
                {
                    UserId = userId,
                    BankAccountId = bankAccountId,
                    From = from,
                    To = to,
                    Search = search,
                    CategoryId = categoryId,
                    MinAmount = minAmount,
                    MaxAmount = maxAmount,
                    TransactionType = transactionType,
                    PageSize = pageSize,
                    Offset = offset
                },
                cancellationToken: cancellationToken)))
            .AsList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        _logger.LogInformation("Returning {Count} transactions for user {UserId} (page {Page}/{TotalPages})",
            items.Count, userId, page, totalPages);

        return new PaginatedTransactionsResponse(items, totalCount, page, pageSize, totalPages);
    }

    /// <inheritdoc />
    public async Task<SpendingTrendResponse> GetSpendingTrendsAsync(
        Guid userId,
        Guid? bankAccountId,
        string period,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting spending trends for user {UserId}, period: {Period}", userId, period);

        var trunc = period.ToLowerInvariant() switch
        {
            "monthly" => "month",
            "quarterly" => "quarter",
            "yearly" => "year",
            _ => "month"
        };

        var dataPoints = await _dbExecutor.QueryAsync<SpendingTrendPoint>(
            new CommandDefinition(
                $"""
                SELECT
                    TO_CHAR(DATE_TRUNC('{trunc}', t.transaction_date), 'YYYY-MM-DD') AS Period,
                    COALESCE(c.name, 'Uncategorised') AS Category,
                    SUM(t.amount) AS Total
                FROM statement_transactions t
                JOIN bank_statements s ON s.id = t.bank_statement_id
                LEFT JOIN transaction_categories c ON c.id = t.category_id
                WHERE s.user_id = @UserId
                AND t.transaction_type = 'debit'
                AND (@BankAccountId IS NULL OR t.bank_account_id = @BankAccountId)
                AND (@From::date IS NULL OR t.transaction_date >= @From::date)
                AND (@To::date IS NULL OR t.transaction_date <= @To::date)
                GROUP BY DATE_TRUNC('{trunc}', t.transaction_date), COALESCE(c.name, 'Uncategorised')
                ORDER BY Period
                """,
                new { UserId = userId, BankAccountId = bankAccountId, From = from, To = to },
                cancellationToken: cancellationToken));

        var periods = dataPoints.Select(p => p.Period).Distinct().ToList().AsReadOnly();
        var categories = dataPoints.Select(p => p.Category).Distinct().ToList().AsReadOnly();
        var points = dataPoints.ToList().AsReadOnly();

        _logger.LogInformation("Spending trends retrieved for user {UserId}: {Periods} periods, {Categories} categories",
            userId, periods.Count, categories.Count);

        return new SpendingTrendResponse(periods, categories, points);
    }

    /// <summary>
    /// Builds a safe ORDER BY clause from user-supplied sort parameters.
    /// Only whitelisted column names and directions are accepted.
    /// </summary>
    private static string BuildOrderByClause(string? sortBy, string? sortOrder)
    {
        var validColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = "t.transaction_date",
            ["amount"] = "t.amount",
            ["description"] = "t.description",
            ["type"] = "t.transaction_type",
            ["category"] = "c.name",
        };

        var dir = sortOrder?.ToLowerInvariant() switch
        {
            "asc" => "ASC",
            "desc" => "DESC",
            _ => "DESC" // default
        };

        if (sortBy is not null && validColumns.TryGetValue(sortBy, out var column))
        {
            return $"{column} {dir}, t.created_at DESC";
        }

        // Default sort
        return $"t.transaction_date DESC, t.created_at DESC";
    }

    internal sealed class CashflowTotals
    {
        public DateOnly? PeriodStart { get; init; }
        public DateOnly? PeriodEnd { get; init; }
        public decimal TotalCredit { get; init; }
        public decimal TotalDebit { get; init; }
    }
}
