using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// Request to create a new savings goal.
/// </summary>
public sealed record CreateSavingGoalRequest
{
    /// <summary>The name of the goal.</summary>
    [Required(ErrorMessage = "Goal name is required.")]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    /// <summary>The target amount to save (must be greater than 0).</summary>
    [Required]
    [Range(0.01, 999999999.99, ErrorMessage = "Target amount must be greater than 0.")]
    public decimal TargetAmount { get; init; }

    /// <summary>The currency code (default AUD).</summary>
    public string Currency { get; init; } = "AUD";

    /// <summary>Optional target date.</summary>
    public DateOnly? TargetDate { get; init; }
}
