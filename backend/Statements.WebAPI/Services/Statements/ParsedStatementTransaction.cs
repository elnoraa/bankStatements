namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// A single transaction parsed from a bank statement file.
/// </summary>
/// <param name="TransactionDate">The date the transaction occurred.</param>
/// <param name="Description">The transaction description or merchant name.</param>
/// <param name="Amount">The transaction amount.</param>
/// <param name="TransactionType">The transaction type: "credit" or "debit".</param>
/// <param name="BalanceAfter">The account balance after this transaction, if available.</param>
/// <param name="CategoryName">The inferred spending category name, if any.</param>
public sealed record ParsedStatementTransaction(
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    string TransactionType,
    decimal? BalanceAfter,
    string CategoryName);
