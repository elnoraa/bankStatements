using Asp.Versioning;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Statements.WebAPI.Contracts.Statements;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Controllers.v1;

[ApiController]
[Authorize]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public sealed class StatementsController : ControllerBase
{
    private const long MaxUploadSizeInBytes = 10 * 1024 * 1024;

    private readonly IStatementService _statementService;
    private readonly ILogger<StatementsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatementsController"/> class.
    /// </summary>
    /// <param name="statementService">Service for statement upload and processing.</param>
    /// <param name="logger">Logger instance.</param>
    public StatementsController(IStatementService statementService, ILogger<StatementsController> logger)
    {
        _statementService = statementService;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a bank statement PDF file for processing.
    /// </summary>
    /// <param name="file">The PDF file to upload (max 10 MB).</param>
    /// <param name="bankAccountId">Optional bank account ID to associate with the statement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="StatementUploadResponse"/> with upload status.</returns>
    /// <response code="201">Upload successful.</response>
    /// <response code="400">Invalid request or processing error.</response>
    /// <response code="401">User not authenticated.</response>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxUploadSizeInBytes)]
    [EnableRateLimiting("UploadStrict")]
    public async Task<ActionResult<StatementUploadResponse>> Upload(
        [FromForm] IFormFile? file,
        [FromForm] Guid bankAccountId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            _logger.LogWarning("POST /api/v1/statements/upload - Unauthorized: invalid user id in token");
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        if (bankAccountId == Guid.Empty)
        {
            _logger.LogWarning("Upload rejected - no bank account provided");
            return BadRequest("A bank account must be selected to upload a statement.");
        }

        if (file is null || file.Length == 0)
        {
            _logger.LogWarning("Upload rejected - no file provided or empty file");
            return BadRequest("Upload a non-empty statement file.");
        }

        _logger.LogInformation("POST /api/v1/statements/upload called: UserId={UserId}, FileName={FileName}, Size={Size}",
            userId, file.FileName, file.Length);

        try
        {
            var response = await _statementService.UploadAsync(userId.Value, bankAccountId, file, cancellationToken);
            _logger.LogInformation("Statement uploaded successfully: Id={StatementId}, Status={Status}", response.Id, response.Status);
            return Created($"/api/v1/statements/{response.Id}", response);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning("Statement upload failed: {Message}", exception.Message);
            return BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// Gets the current processing status of a previously uploaded statement.
    /// </summary>
    /// <param name="statementId">The statement ID returned from the upload endpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="StatementUploadResponse"/> with the current status.</returns>
    /// <response code="200">Status retrieved successfully.</response>
    /// <response code="401">User not authenticated.</response>
    /// <response code="404">Statement not found or does not belong to the user.</response>
    [HttpGet("{statementId}")]
    public async Task<ActionResult<StatementUploadResponse>> GetStatement(
        Guid statementId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            _logger.LogWarning("GET /api/v1/statements/{StatementId} - Unauthorized: invalid user id in token", statementId);
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        var statement = await _statementService.GetStatementAsync(
            userId.Value, statementId, cancellationToken);

        if (statement is null)
        {
            _logger.LogWarning("GET /api/v1/statements/{StatementId} - Not found for user {UserId}", statementId, userId);
            return NotFound("Statement not found.");
        }

        _logger.LogInformation("GET /api/v1/statements/{StatementId} - Status={Status}", statementId, statement.Status);
        return Ok(statement);
    }

    /// <summary>
    /// Lists all statements for the current user, ordered by most recent upload first.
    /// </summary>
    /// <param name="page">Page number (1-based, default 1).</param>
    /// <param name="pageSize">Items per page (default 20).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of statement summaries.</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StatementListItemResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        var statements = await _statementService.ListAsync(userId.Value, page, pageSize, cancellationToken);
        return Ok(statements);
    }

    /// <summary>
    /// Retries processing a failed statement by re-queuing it for background processing.
    /// </summary>
    /// <param name="statementId">The ID of the failed statement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK with a confirmation message, or 400 if the statement cannot be retried.</returns>
    [HttpPost("{statementId}/retry")]
    [EnableRateLimiting("UploadRetry")]
    public async Task<ActionResult> Retry(Guid statementId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        try
        {
            await _statementService.RetryAsync(userId.Value, statementId, cancellationToken);
            _logger.LogInformation("Statement {StatementId} retry queued by user {UserId}", statementId, userId);
            return Ok(new { message = "Statement queued for reprocessing." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Statement retry failed: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Uploads multiple bank statement PDF files in a single request for processing.
    /// </summary>
    /// <param name="files">The PDF files to upload (max 10 MB each).</param>
    /// <param name="bankAccountId">The bank account ID to associate with all statements.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="BulkUploadResponse"/> with per-file results.</returns>
    [HttpPost("upload/bulk")]
    [RequestSizeLimit(MaxUploadSizeInBytes * 10)]
    [EnableRateLimiting("UploadStrict")]
    public async Task<ActionResult<BulkUploadResponse>> UploadBulk(
        [FromForm] List<IFormFile>? files,
        [FromForm] Guid bankAccountId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        if (files is null || files.Count == 0)
        {
            return BadRequest("Upload at least one file.");
        }

        _logger.LogInformation("POST /api/v1/statements/upload/bulk called: UserId={UserId}, FileCount={Count}",
            userId, files.Count);

        try
        {
            var result = await _statementService.UploadMultipleAsync(
                userId.Value, bankAccountId, files, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Bulk upload failed: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Deletes a statement and its associated transactions.
    /// </summary>
    /// <param name="statementId">The statement ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK with a confirmation message.</returns>
    [HttpDelete("{statementId}")]
    public async Task<ActionResult> Delete(Guid statementId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        try
        {
            await _statementService.DeleteAsync(userId.Value, statementId, cancellationToken);
            _logger.LogInformation("Statement {StatementId} deleted by user {UserId}", statementId, userId);
            return Ok(new { message = "Statement deleted successfully." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Delete failed: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Downloads the original uploaded file for a statement.
    /// </summary>
    /// <param name="statementId">The statement ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The original PDF file.</returns>
    [HttpGet("{statementId}/download")]
    public async Task<IActionResult> Download(Guid statementId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        try
        {
            var (filePath, originalFileName) = await _statementService.DownloadOriginalAsync(
                userId.Value, statementId, cancellationToken);
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "application/pdf", originalFileName);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Download failed: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Archives a statement, removing it from the active list but preserving data.
    /// </summary>
    /// <param name="statementId">The statement ID to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK with a confirmation message.</returns>
    [HttpPost("{statementId}/archive")]
    public async Task<ActionResult> Archive(Guid statementId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        try
        {
            await _statementService.ArchiveAsync(userId.Value, statementId, cancellationToken);
            _logger.LogInformation("Statement {StatementId} archived by user {UserId}", statementId, userId);
            return Ok(new { message = "Statement archived successfully." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Archive failed: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Lists archived statements for the current user.
    /// </summary>
    /// <param name="page">Page number (1-based, default 1).</param>
    /// <param name="pageSize">Items per page (default 20).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of archived statement summaries.</returns>
    [HttpGet("archived")]
    public async Task<ActionResult<IReadOnlyList<StatementListItemResponse>>> ListArchived(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        var statements = await _statementService.ListArchivedAsync(userId.Value, page, pageSize, cancellationToken);
        return Ok(statements);
    }

    /// <summary>
    /// Extracts the current user's ID from the JWT claims in the authorization header.
    /// </summary>
    /// <returns>The user's <see cref="Guid"/> if found and valid; otherwise <c>null</c>.</returns>
    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subject, out var userId))
        {
            return null;
        }

        return userId;
    }
}
