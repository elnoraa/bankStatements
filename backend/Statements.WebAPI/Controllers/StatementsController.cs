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

    public StatementsController(IStatementService statementService)
    {
        _statementService = statementService;
    }

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
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest("Upload a non-empty statement file.");
        }

        try
        {
            var response = await _statementService.UploadAsync(userId.Value, bankAccountId, file, cancellationToken);
            return Created($"/api/statements/{response.Id}", response);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

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
