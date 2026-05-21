namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// Budget vs. actual spending for a single category in a given month.
/// </summary>
public sealed class BudgetProgressResponse
{
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = null!;
    public decimal BudgetAmount { get; init; }
    public decimal ActualSpending { get; init; }
    public decimal PercentageUsed { get; init; }
    public decimal Remaining { get; init; }
    public bool IsOverBudget { get; init; }
}
