namespace Statements.WebAPI.Contracts.Analysis;

public sealed record RecentTransactionResponse(
    Guid Id,
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    string TransactionType,
    string? Category);
