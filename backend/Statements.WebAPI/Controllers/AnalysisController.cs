using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Services.Analysis;

namespace Statements.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class AnalysisController : ControllerBase
{
    private readonly IAnalysisService _analysisService;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(IAnalysisService analysisService, ILogger<AnalysisController> logger)
    {
        _analysisService = analysisService;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<SpendingSummaryResponse>> GetSummary(
        [FromQuery] Guid? bankAccountId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
        {
            _logger.LogWarning("GET /api/analysis/summary - Unauthorized: invalid user id in token");
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        _logger.LogInformation(
            "GET /api/analysis/summary called: UserId={UserId}, BankAccountId={BankAccountId}, From={From}, To={To}",
            userId, bankAccountId, from, to);

        if (from is not null && to is not null && from > to)
        {
            _logger.LogWarning("Invalid date range: from ({From}) > to ({To})", from, to);
            return BadRequest("'from' must be before or equal to 'to'.");
        }

        var result = await _analysisService.GetSummaryAsync(
            userId.Value,
            bankAccountId,
            from,
            to,
            cancellationToken);

        _logger.LogInformation("Spending summary returned for user {UserId}: {TransactionCount} recent transactions",
            userId, result.RecentTransactions.Count);

        return Ok(result);
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
