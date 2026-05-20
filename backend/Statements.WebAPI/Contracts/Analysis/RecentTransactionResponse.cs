namespace Statements.WebAPI.Contracts.Analysis;

public sealed class RecentTransactionResponse
{
    public Guid Id { get; init; }
    public DateOnly TransactionDate { get; init; }
    public string Description { get; init; } = null!;
    public decimal Amount { get; init; }
    public string TransactionType { get; init; } = null!;
    public string? Category { get; init; }
}
