using Statements.WebAPI.Contracts.Statements;

namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// Provides operations for uploading and managing bank statements.
/// </summary>
public interface IStatementService
{
    /// <summary>
    /// Uploads a bank statement file, performs virus scanning, and publishes a
    /// message for background PDF parsing and transaction extraction.
    /// </summary>
    /// <param name="userId">The ID of the user uploading the statement.</param>
    /// <param name="bankAccountId">The bank account ID to associate with the statement.</param>
    /// <param name="file">The uploaded file (PDF format expected).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="StatementUploadResponse"/> with the upload result (status will be "uploaded").</returns>
    Task<StatementUploadResponse> UploadAsync(
        Guid userId,
        Guid bankAccountId,
        IFormFile file,
        CancellationToken cancellationToken);

    /// <summary>
    /// Uploads multiple bank statement files, processing each through the same pipeline
    /// as single uploads. Results are collected per file.
    /// </summary>
    /// <param name="userId">The ID of the user uploading the statements.</param>
    /// <param name="bankAccountId">The bank account ID to associate with all statements.</param>
    /// <param name="files">The list of uploaded files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="BulkUploadResponse"/> with per-file results.</returns>
    Task<BulkUploadResponse> UploadMultipleAsync(
        Guid userId,
        Guid bankAccountId,
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current state of a statement by ID, including the computed
    /// transaction count. Used by the frontend for polling after an async upload.
    /// </summary>
    /// <param name="userId">The ID of the owning user.</param>
    /// <param name="statementId">The statement ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The statement state, or null if not found or not owned by the user.</returns>
    Task<StatementUploadResponse?> GetStatementAsync(
        Guid userId,
        Guid statementId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists all statements for a user, ordered by most recent upload first.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of statement summaries.</returns>
    Task<IReadOnlyList<StatementListItemResponse>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retries processing a failed statement by resetting its status and
    /// re-publishing a processing message to the queue.
    /// </summary>
    /// <param name="userId">The ID of the owning user.</param>
    /// <param name="statementId">The statement ID to retry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if the statement is not in a failed state or not found.</exception>
    Task RetryAsync(
        Guid userId,
        Guid statementId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a statement and its associated transactions, and removes the stored file from disk.
    /// </summary>
    /// <param name="userId">The ID of the owning user.</param>
    /// <param name="statementId">The statement ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if the statement is not found or not owned by the user.</exception>
    Task DeleteAsync(
        Guid userId,
        Guid statementId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the original uploaded file for a statement.
    /// </summary>
    /// <param name="userId">The ID of the owning user.</param>
    /// <param name="statementId">The statement ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the file path and the original file name.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the statement is not found or the file is missing.</exception>
    Task<(string FilePath, string OriginalFileName)> DownloadOriginalAsync(
        Guid userId,
        Guid statementId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Archives a statement, removing it from the active list but preserving its data.
    /// </summary>
    /// <param name="userId">The ID of the owning user.</param>
    /// <param name="statementId">The statement ID to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if the statement is not found or not owned by the user.</exception>
    Task ArchiveAsync(
        Guid userId,
        Guid statementId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists archived statements for a user.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of archived statement summaries.</returns>
    Task<IReadOnlyList<StatementListItemResponse>> ListArchivedAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
