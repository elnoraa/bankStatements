namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// A user-defined savings goal.
/// </summary>
public sealed record SavingGoalResponse
{
    /// <summary>The unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The goal name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The target amount to save.</summary>
    public decimal TargetAmount { get; init; }

    /// <summary>The current amount saved.</summary>
    public decimal CurrentAmount { get; init; }

    /// <summary>The currency code.</summary>
    public string Currency { get; init; } = "AUD";

    /// <summary>Optional target date to reach the goal.</summary>
    public DateOnly? TargetDate { get; init; }

    /// <summary>Whether the goal has been completed.</summary>
    public bool IsCompleted { get; init; }

    /// <summary>Calculated progress percentage (0-100).</summary>
    public decimal ProgressPercent { get; init; }
}
