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
}
