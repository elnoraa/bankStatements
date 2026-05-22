using Asp.Versioning;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Services.Analysis;

namespace Statements.WebAPI.Controllers.v1;

[ApiController]
[Authorize]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public sealed class BudgetsController : ControllerBase
{
    private readonly IBudgetService _budgetService;
    private readonly ILogger<BudgetsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BudgetsController"/> class.
    /// </summary>
    public BudgetsController(IBudgetService budgetService, ILogger<BudgetsController> logger)
    {
        _budgetService = budgetService;
        _logger = logger;
    }

    /// <summary>
    /// Lists all budgets for the current user in the specified month.
    /// </summary>
    /// <param name="year">The year (e.g., 2026).</param>
    /// <param name="month">The month (1-12).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of budgets for the month.</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BudgetResponse>>> List(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var monthYear = new DateOnly(year, month, 1);
        var budgets = await _budgetService.ListAsync(userId.Value, monthYear, cancellationToken);
        return Ok(budgets);
    }

    /// <summary>
    /// Creates or updates a monthly budget for a category (upserts on user + category + month).
    /// </summary>
    /// <param name="request">The budget details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created or updated budget.</returns>
    [HttpPost]
    public async Task<ActionResult<BudgetResponse>> CreateOrUpdate(
        [FromBody] CreateBudgetRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var budget = await _budgetService.CreateOrUpdateAsync(userId.Value, request, cancellationToken);
        _logger.LogInformation("Budget set for category {CategoryId}, month {MonthYear}: {Amount}",
            request.CategoryId, request.MonthYear, request.Amount);
        return Ok(budget);
    }

    /// <summary>
    /// Deletes a budget by ID.
    /// </summary>
    /// <param name="id">The budget ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>204 No Content on success, 404 if not found.</returns>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        try
        {
            await _budgetService.DeleteAsync(userId.Value, id, cancellationToken);
            _logger.LogInformation("Budget {BudgetId} deleted by user {UserId}", id, userId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Gets budget vs. actual spending for each budgeted category in the given month.
    /// </summary>
    /// <param name="year">The year (e.g., 2026).</param>
    /// <param name="month">The month (1-12).</param>
    /// <param name="bankAccountId">Optional bank account filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Budget progress with percentage used and remaining amounts.</returns>
    [HttpGet("progress")]
    public async Task<ActionResult<IReadOnlyList<BudgetProgressResponse>>> GetProgress(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] Guid? bankAccountId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var monthYear = new DateOnly(year, month, 1);
        var progress = await _budgetService.GetProgressAsync(userId.Value, monthYear, bankAccountId, cancellationToken);
        return Ok(progress);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subject, out var userId))
            return null;

        return userId;
    }
}
