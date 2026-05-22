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
[Route("api/v{version:apiVersion}/saving-goals")]
[ApiVersion("1.0")]
public sealed class SavingGoalsController : ControllerBase
{
    private readonly ISavingGoalService _savingGoalService;
    private readonly ILogger<SavingGoalsController> _logger;

    public SavingGoalsController(ISavingGoalService savingGoalService, ILogger<SavingGoalsController> logger)
    {
        _savingGoalService = savingGoalService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavingGoalResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var goals = await _savingGoalService.ListAsync(userId.Value, cancellationToken);
        return Ok(goals);
    }

    [HttpPost]
    public async Task<ActionResult<SavingGoalResponse>> Create(CreateSavingGoalRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        try
        {
            var goal = await _savingGoalService.CreateAsync(userId.Value, request, cancellationToken);
            return Created($"/api/v1/saving-goals/{goal.Id}", goal);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{goalId}")]
    public async Task<ActionResult<SavingGoalResponse>> Update(Guid goalId, UpdateSavingGoalRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        try
        {
            var goal = await _savingGoalService.UpdateAsync(userId.Value, goalId, request, cancellationToken);
            return Ok(goal);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{goalId}")]
    public async Task<ActionResult> Delete(Guid goalId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        try
        {
            await _savingGoalService.DeleteAsync(userId.Value, goalId, cancellationToken);
            return Ok(new { message = "Saving goal deleted successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var userId)) return null;
        return userId;
    }
}
