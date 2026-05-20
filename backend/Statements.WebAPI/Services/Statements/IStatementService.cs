using Statements.WebAPI.Contracts.Statements;

namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// Provides operations for uploading and managing bank statements.
/// </summary>
public interface IStatementService
{
    /// <summary>
    /// Uploads a bank statement file, performs virus scanning, and initiates parsing.
    /// </summary>
    /// <param name="userId">The ID of the user uploading the statement.</param>
    /// <param name="bankAccountId">Optional bank account ID to associate with the statement.</param>
    /// <param name="file">The uploaded file (PDF format expected).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="StatementUploadResponse"/> with the result of the upload.</returns>
    Task<StatementUploadResponse> UploadAsync(
        Guid userId,
        Guid? bankAccountId,
        IFormFile file,
        CancellationToken cancellationToken);
}
