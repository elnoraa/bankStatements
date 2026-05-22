using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// Request to update a parsed transaction's fields.
/// All properties are optional — only provided values are updated.
/// </summary>
public sealed class UpdateTransactionRequest
{
    /// <summary>Updated transaction description, if provided.</summary>
    [MaxLength(500)]
    public string? Description { get; init; }

    /// <summary>Updated transaction amount, if provided.</summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// Updated category ID, if provided.
    /// Set to null to uncategorise the transaction.
    /// </summary>
    public Guid? CategoryId { get; init; }
}
