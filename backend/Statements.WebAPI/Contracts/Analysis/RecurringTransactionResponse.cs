namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// A detected recurring transaction pattern.
/// </summary>
public sealed record RecurringTransactionResponse
{
    /// <summary>The unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The transaction description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The recurring amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>The detected frequency pattern.</summary>
    public string Frequency { get; init; } = string.Empty;

    /// <summary>The associated category name, if any.</summary>
    public string? Category { get; init; }

    /// <summary>The day of month/week for the next expected occurrence.</summary>
    public int? ExpectedDay { get; init; }

    /// <summary>The last date this transaction was detected.</summary>
    public DateOnly? LastDetected { get; init; }

    /// <summary>Whether the recurring transaction is still active.</summary>
    public bool IsActive { get; init; }
}
