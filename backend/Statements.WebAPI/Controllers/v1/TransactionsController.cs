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
public sealed class TransactionsController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        ITransactionService transactionService,
        ILogger<TransactionsController> logger)
    {
        _transactionService = transactionService;
        _logger = logger;
    }

    /// <summary>
    /// Updates a transaction's editable fields (description, amount, category).
    /// </summary>
    /// <param name="id">The transaction ID.</param>
    /// <param name="request">The fields to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid id,
        [FromBody] UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        try
        {
            await _transactionService.UpdateAsync(userId.Value, id, request, cancellationToken);
            _logger.LogInformation("Transaction {TransactionId} updated by user {UserId}", id, userId);
            return Ok(new { message = "Transaction updated." });
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

        if (!Guid.TryParse(subject, out var userId))
        {
            return null;
        }

        return userId;
    }
}
