namespace Statements.WebAPI.Contracts.Statements;

/// <summary>
/// Response returned after a bank statement file has been uploaded.
/// </summary>
public sealed class StatementUploadResponse
{
    /// <summary>
    /// The unique identifier of the uploaded statement.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The ID of the user who uploaded the statement.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// The associated bank account ID, if identified.
    /// </summary>
    public Guid? BankAccountId { get; init; }

    /// <summary>
    /// The original file name as provided by the user.
    /// </summary>
    public string OriginalFileName { get; init; } = null!;

    /// <summary>
    /// The unique file name used for storage on the server.
    /// </summary>
    public string StoredFileName { get; init; } = null!;

    /// <summary>
    /// SHA-256 hash of the file contents for deduplication.
    /// </summary>
    public string FileHash { get; init; } = null!;

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long SizeInBytes { get; init; }

    /// <summary>
    /// The MIME content type of the uploaded file.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// The processing status of the statement (e.g., "uploaded", "processing", "completed").
    /// </summary>
    public string Status { get; init; } = null!;

    /// <summary>
    /// The date and time when the statement was uploaded.
    /// </summary>
    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>
    /// The number of transactions parsed from the statement.
    /// </summary>
    public int ParsedTransactionCount { get; init; }

    /// <summary>
    /// The date and time when background processing completed.
    /// Null while the statement is still being processed.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; init; }

    /// <summary>
    /// If processing failed, a human-readable error message.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
