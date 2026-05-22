namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// Paginated list of transactions.
/// </summary>
/// <param name="Items">The transactions on this page.</param>
/// <param name="TotalCount">Total number of transactions across all pages.</param>
/// <param name="Page">Current page number (1-based).</param>
/// <param name="PageSize">Number of items per page.</param>
/// <param name="TotalPages">Total number of pages.</param>
public sealed record PaginatedTransactionsResponse(
    IReadOnlyList<RecentTransactionResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
