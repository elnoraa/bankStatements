using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// Request to update an existing savings goal.
/// </summary>
public sealed record UpdateSavingGoalRequest
{
    /// <summary>The updated name (optional).</summary>
    [MaxLength(200)]
    public string? Name { get; init; }

    /// <summary>The updated target amount (optional, must be > 0 if provided).</summary>
    [Range(0.01, 999999999.99, ErrorMessage = "Target amount must be greater than 0.")]
    public decimal? TargetAmount { get; init; }

    /// <summary>The updated current amount saved.</summary>
    [Range(0, 999999999.99)]
    public decimal? CurrentAmount { get; init; }

    /// <summary>The updated currency code (optional).</summary>
    public string? Currency { get; init; }

    /// <summary>Optional target date.</summary>
    public DateOnly? TargetDate { get; init; }
}
