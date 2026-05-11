namespace Statements.WebAPI.Contracts.Statements;

public sealed record StatementUploadResponse(
    Guid Id,
    Guid UserId,
    Guid? BankAccountId,
    string OriginalFileName,
    string StoredFileName,
    long SizeInBytes,
    string? ContentType,
    string Status,
    DateTimeOffset UploadedAt,
    int ParsedTransactionCount);
