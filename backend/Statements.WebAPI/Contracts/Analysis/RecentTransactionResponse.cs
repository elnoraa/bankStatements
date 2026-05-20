namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// A single recent transaction with basic details.
/// </summary>
public sealed class RecentTransactionResponse
{
    /// <summary>
    /// The unique identifier of the transaction.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The date the transaction occurred.
    /// </summary>
    public DateOnly TransactionDate { get; init; }

    /// <summary>
    /// The transaction description or merchant name.
    /// </summary>
    public string Description { get; init; } = null!;

    /// <summary>
    /// The transaction amount.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// The transaction type: "credit" or "debit".
    /// </summary>
    public string TransactionType { get; init; } = null!;

    /// <summary>
    /// The assigned spending category, if any.
    /// </summary>
    public string? Category { get; init; }
}
