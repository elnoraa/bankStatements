namespace Statements.WebAPI.Contracts.Analysis;

public sealed class CategorySpendingResponse
{
    public string Category { get; init; } = null!;
    public decimal TotalDebit { get; init; }
    public int TransactionCount { get; init; }
}
