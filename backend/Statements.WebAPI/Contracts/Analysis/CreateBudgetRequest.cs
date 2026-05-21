using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// Request to create or update a monthly budget for a category.
/// </summary>
public sealed class CreateBudgetRequest
{
    /// <summary>The category to budget for.</summary>
    [Required]
    public Guid CategoryId { get; init; }

    /// <summary>First day of the budget month, e.g. 2026-05-01.</summary>
    [Required]
    public DateOnly MonthYear { get; init; }

    /// <summary>The budgeted amount (must be positive).</summary>
    [Required]
    [Range(0.01, 999999999.99)]
    public decimal Amount { get; init; }
}
