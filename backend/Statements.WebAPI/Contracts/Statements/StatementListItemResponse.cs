namespace Statements.WebAPI.Contracts.Statements;

/// <summary>
/// A summary of a single bank statement, as shown in the statement list.
/// </summary>
public sealed class StatementListItemResponse
{
    public Guid Id { get; init; }
    public string OriginalFileName { get; init; } = null!;
    public string Status { get; init; } = null!;
    public DateTimeOffset UploadedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
    public DateTimeOffset? FailedAt { get; init; }
    public int ParsedTransactionCount { get; init; }
    public long SizeInBytes { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid BankAccountId { get; init; }
}
