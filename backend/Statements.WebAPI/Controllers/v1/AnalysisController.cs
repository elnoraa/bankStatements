using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Services.Analysis;

namespace Statements.WebAPI.Controllers.v1;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public sealed class AnalysisController : ControllerBase
{
    private readonly IAnalysisService _analysisService;
    private readonly ILogger<AnalysisController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisController"/> class.
    /// </summary>
    /// <param name="analysisService">Service for spending analysis operations.</param>
    /// <param name="logger">Logger instance.</param>
    public AnalysisController(IAnalysisService analysisService, ILogger<AnalysisController> logger)
    {
        _analysisService = analysisService;
        _logger = logger;
    }

    /// <summary>
    /// Gets a spending summary with category breakdown and recent transactions.
    /// </summary>
    /// <param name="bankAccountId">Optional bank account ID to filter by.</param>
    /// <param name="from">Optional start date for the analysis period.</param>
    /// <param name="to">Optional end date for the analysis period.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SpendingSummaryResponse"/> with aggregated spending data.</returns>
    /// <response code="200">Summary retrieved successfully.</response>
    /// <response code="400">Invalid date range (from &gt; to).</response>
    /// <response code="401">User not authenticated.</response>
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
            _logger.LogWarning("GET /api/v1/analysis/summary - Unauthorized: invalid user id in token");
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        _logger.LogInformation(
            "GET /api/v1/analysis/summary called: UserId={UserId}, BankAccountId={BankAccountId}, From={From}, To={To}",
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
