namespace Statements.WebAPI.Services.Statements;

public sealed record ParsedStatementTransaction(
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    string TransactionType,
    decimal? BalanceAfter,
    string CategoryName);
