namespace Statements.WebAPI.Contracts.Messages;

/// <summary>
/// Message published after a statement file has been validated, virus-scanned,
/// saved to disk, and inserted into the database. Instructs the background
/// consumer to parse the PDF and insert the extracted transactions.
/// </summary>
public sealed record ProcessStatementMessage
{
    public Guid StatementId { get; init; }
    public string StoredFileName { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public Guid BankAccountId { get; init; }
}
