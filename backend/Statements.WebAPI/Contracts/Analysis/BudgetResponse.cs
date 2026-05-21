namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// A monthly budget for a spending category.
/// </summary>
public sealed class BudgetResponse
{
    public Guid Id { get; init; }
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = null!;
    public DateOnly MonthYear { get; init; }
    public decimal Amount { get; init; }
}
