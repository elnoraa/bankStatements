using Asp.Versioning;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Statements.WebAPI.Contracts.Basiq;

namespace Statements.WebAPI.Services.Basiq;

[ApiController]
[Authorize]
[Route("api/v{version:apiVersion}/basiq")]
[ApiVersion("1.0")]
public sealed class BasiqController : ControllerBase
{
    private readonly IBasiqService _basiqService;
    private readonly ILogger<BasiqController> _logger;

    public BasiqController(IBasiqService basiqService, ILogger<BasiqController> logger)
    {
        _basiqService = basiqService;
        _logger = logger;
    }

    /// <summary>
    /// Initiates a new Basiq bank connection. Returns the consent UI URL to redirect the user to.
    /// </summary>
    [HttpPost("connections")]
    [EnableRateLimiting("BasiqRefresh")]
    public async Task<ActionResult<InitiateConnectionResponse>> InitiateConnection(
        [FromBody] InitiateConnectionRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized("Authenticated user id is missing or invalid.");

        if (string.IsNullOrWhiteSpace(request.InstitutionName))
            return BadRequest("Institution name is required.");

        try
        {
            var result = await _basiqService.InitiateConnectionAsync(userId.Value, request, ct);
            _logger.LogInformation(
                "Basiq connection initiated for user {UserId}, connection {ConnectionId}",
                userId, result.ConnectionId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Basiq connection initiation failed: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Completes a connection after the user grants consent via the Basiq consent UI.
    /// Called with the job ID from the consent callback.
    /// </summary>
    [HttpPost("connections/callback")]
    public async Task<ActionResult<BasiqConnectionResponse>> CompleteConnection(
        [FromBody] CompleteConnectionRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized("Authenticated user id is missing or invalid.");

        if (string.IsNullOrWhiteSpace(request.JobId))
            return BadRequest("Job ID is required.");

        try
        {
            var result = await _basiqService.CompleteConnectionAsync(
                userId.Value, request, ct);
            _logger.LogInformation(
                "Basiq connection {ConnectionId} completed for user {UserId}",
                request.ConnectionId, userId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "Basiq connection completion failed: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Lists all Basiq connections for the authenticated user.
    /// </summary>
    [HttpGet("connections")]
    public async Task<ActionResult<BasiqConnectionListResponse>> ListConnections(
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized("Authenticated user id is missing or invalid.");

        var result = await _basiqService.GetConnectionsAsync(userId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// Gets a single Basiq connection by ID.
    /// </summary>
    [HttpGet("connections/{id:guid}")]
    public async Task<ActionResult<BasiqConnectionResponse>> GetConnection(
        Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized("Authenticated user id is missing or invalid.");

        try
        {
            var result = await _basiqService.GetConnectionAsync(userId.Value, id, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Updates the sync configuration for a connection (enabled, frequency).
    /// </summary>
    [HttpPut("connections/{id:guid}/sync")]
    public async Task<ActionResult<BasiqConnectionResponse>> UpdateSyncConfig(
        Guid id,
        [FromBody] UpdateSyncConfigRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized("Authenticated user id is missing or invalid.");

        try
        {
            var result = await _basiqService.UpdateSyncConfigAsync(
                userId.Value, id, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Triggers a manual sync of the connection's transactions from Basiq.
    /// </summary>
    [HttpPost("connections/{id:guid}/refresh")]
    [EnableRateLimiting("BasiqRefresh")]
    public async Task<ActionResult<BasiqConnectionResponse>> RefreshConnection(
        Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized("Authenticated user id is missing or invalid.");

        try
        {
            var result = await _basiqService.RefreshConnectionAsync(
                userId.Value, id, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Removes a Basiq connection. Imported transactions are preserved.
    /// </summary>
    [HttpDelete("connections/{id:guid}")]
    public async Task<ActionResult> RemoveConnection(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized("Authenticated user id is missing or invalid.");

        try
        {
            await _basiqService.RemoveConnectionAsync(userId.Value, id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Gets the sync history log for a connection.
    /// </summary>
    [HttpGet("connections/{id:guid}/sync-log")]
    public async Task<ActionResult<IReadOnlyList<SyncLogResponse>>> GetSyncLog(
        Guid id,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized("Authenticated user id is missing or invalid.");

        try
        {
            var result = await _basiqService.GetSyncLogAsync(
                userId.Value, id, limit, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
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
