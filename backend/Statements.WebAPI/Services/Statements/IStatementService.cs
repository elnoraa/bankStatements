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
}
