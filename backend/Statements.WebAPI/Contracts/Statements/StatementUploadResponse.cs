namespace Statements.WebAPI.Contracts.Statements;

public sealed class StatementUploadResponse
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid? BankAccountId { get; init; }
    public string OriginalFileName { get; init; } = null!;
    public string StoredFileName { get; init; } = null!;
    public string FileHash { get; init; } = null!;
    public long SizeInBytes { get; init; }
    public string? ContentType { get; init; }
    public string Status { get; init; } = null!;
    public DateTimeOffset UploadedAt { get; init; }
    public int ParsedTransactionCount { get; init; }
}
