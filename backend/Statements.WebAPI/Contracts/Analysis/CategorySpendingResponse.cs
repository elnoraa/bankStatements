namespace Statements.WebAPI.Contracts.Analysis;

public sealed record CategorySpendingResponse(
    string Category,
    decimal TotalDebit,
    int TransactionCount);
