namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// Spending breakdown for a single category.
/// </summary>
public sealed class CategorySpendingResponse
{
    /// <summary>
    /// The spending category name (e.g., "Groceries", "Utilities").
    /// </summary>
    public string Category { get; init; } = null!;

    /// <summary>
    /// Total debit amount spent in this category.
    /// </summary>
    public decimal TotalDebit { get; init; }

    /// <summary>
    /// Number of transactions in this category.
    /// </summary>
    public int TransactionCount { get; init; }
}
