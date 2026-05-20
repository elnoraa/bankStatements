using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Statements.WebAPI.Contracts.Statements;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
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
    public async Task<ActionResult<StatementUploadResponse>> Upload(
        [FromForm] IFormFile? file,
        [FromForm] Guid? bankAccountId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            _logger.LogWarning("POST /api/statements/upload - Unauthorized: invalid user id in token");
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        if (file is null || file.Length == 0)
        {
            _logger.LogWarning("Upload rejected - no file provided or empty file");
            return BadRequest("Upload a non-empty statement file.");
        }

        _logger.LogInformation("POST /api/statements/upload called: UserId={UserId}, FileName={FileName}, Size={Size}",
            userId, file.FileName, file.Length);

        try
        {
            var response = await _statementService.UploadAsync(userId.Value, bankAccountId, file, cancellationToken);
            _logger.LogInformation("Statement uploaded successfully: Id={StatementId}, Status={Status}", response.Id, response.Status);
            return Created($"/api/statements/{response.Id}", response);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning("Statement upload failed: {Message}", exception.Message);
            return BadRequest(exception.Message);
        }
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
