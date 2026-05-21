namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// A transaction category that can be assigned to transactions.
/// </summary>
public sealed class CategoryResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string TransactionType { get; init; } = null!;
}
