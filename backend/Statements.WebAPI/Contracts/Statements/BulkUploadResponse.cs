namespace Statements.WebAPI.Contracts.Statements;

/// <summary>
/// Response returned after a bulk upload of multiple statement files.
/// </summary>
public sealed class BulkUploadResponse
{
    /// <summary>
    /// Results for each file in the upload batch, in order.
    /// </summary>
    public IReadOnlyList<SingleFileUploadResult> Results { get; init; } = Array.Empty<SingleFileUploadResult>();

    /// <summary>
    /// Total number of files in the batch that succeeded.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Total number of files in the batch that failed.
    /// </summary>
    public int FailureCount { get; init; }
}

/// <summary>
/// Upload result for a single file in a bulk upload.
/// </summary>
public sealed class SingleFileUploadResult
{
    /// <summary>
    /// The original file name.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// True if the file was uploaded successfully; false if it failed.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The statement upload response, if the upload succeeded.
    /// </summary>
    public StatementUploadResponse? Response { get; init; }

    /// <summary>
    /// Error message, if the upload failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
