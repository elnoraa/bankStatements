namespace Statements.WebAPI.Contracts.Basiq;

public sealed class SyncLogResponse
{
    public Guid Id { get; init; }
    public string Status { get; init; } = string.Empty;
    public int TransactionsFetched { get; init; }
    public int TransactionsInserted { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset SyncedAt { get; init; }
}
