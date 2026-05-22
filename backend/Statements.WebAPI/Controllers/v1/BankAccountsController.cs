using Asp.Versioning;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Statements.WebAPI.Contracts.BankAccounts;
using Statements.WebAPI.Services.BankAccounts;

namespace Statements.WebAPI.Controllers.v1;

[ApiController]
[Authorize]
[Route("api/v{version:apiVersion}/bank-accounts")]
[ApiVersion("1.0")]
public sealed class BankAccountsController : ControllerBase
{
    private readonly IBankAccountService _bankAccountService;
    private readonly ILogger<BankAccountsController> _logger;

    public BankAccountsController(IBankAccountService bankAccountService, ILogger<BankAccountsController> logger)
    {
        _bankAccountService = bankAccountService;
        _logger = logger;
    }

    /// <summary>
    /// Lists all bank accounts for the authenticated user.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BankAccountResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        var accounts = await _bankAccountService.ListAsync(userId.Value, cancellationToken);
        return Ok(accounts);
    }

    /// <summary>
    /// Creates a new bank account for the authenticated user.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BankAccountResponse>> Create(
        [FromBody] CreateBankAccountRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        var account = await _bankAccountService.CreateAsync(userId.Value, request, cancellationToken);
        return Created($"/api/v1/bank-accounts/{account.Id}", account);
    }

    /// <summary>
    /// Updates the name of an existing bank account.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BankAccountResponse>> Update(
        Guid id,
        [FromBody] UpdateBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        try
        {
            var account = await _bankAccountService.UpdateAsync(userId.Value, id, request, cancellationToken);
            return Ok(account);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// Deletes a bank account and all associated statements and transactions.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        try
        {
            await _bankAccountService.DeleteAsync(userId.Value, id, cancellationToken);
            return NoContent();
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
