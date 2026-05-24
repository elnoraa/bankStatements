using Asp.Versioning;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Services.Analysis;
using Statements.WebAPI.Services.Export;

namespace Statements.WebAPI.Controllers.v1;

[ApiController]
[Authorize]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public sealed class AnalysisController : ControllerBase
{
    private readonly IAnalysisService _analysisService;
    private readonly ITransactionService _transactionService;
    private readonly ICsvExportService _csvExportService;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(
        IAnalysisService analysisService,
        ITransactionService transactionService,
        ICsvExportService csvExportService,
        ILogger<AnalysisController> logger)
    {
        _analysisService = analysisService;
        _transactionService = transactionService;
        _csvExportService = csvExportService;
        _logger = logger;
    }

    /// <summary>
    /// Gets a spending summary with category breakdown and recent transactions.
    /// </summary>
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
    /// Returns all available transaction categories for editing transactions.
    /// </summary>
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetCategories(CancellationToken cancellationToken)
    {
        var categories = await _transactionService.GetCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    /// <summary>
    /// Downloads transactions as a CSV file for the current user and filters.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid? bankAccountId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized("Authenticated user id is missing or invalid.");
        }

        var csvBytes = await _csvExportService.ExportTransactionsAsync(
            userId.Value, bankAccountId, from, to, cancellationToken);

        return File(csvBytes, "text/csv", $"transactions-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    /// <summary>
    /// Gets transactions with pagination support.
    /// </summary>
    [HttpGet("transactions")]
    public async Task<ActionResult<PaginatedTransactionsResponse>> GetTransactions(
        [FromQuery] Guid? bankAccountId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] decimal? minAmount,
        [FromQuery] decimal? maxAmount,
        [FromQuery] string? transactionType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        if (string.IsNullOrWhiteSpace(search)) search = null;
        if (string.IsNullOrWhiteSpace(transactionType)) transactionType = null;

        var result = await _analysisService.GetTransactionsAsync(
            userId.Value, bankAccountId, from, to,
            search, categoryId, minAmount, maxAmount, transactionType,
            page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Gets spending trends aggregated by period.
    /// </summary>
    [HttpGet("trends")]
    public async Task<ActionResult<SpendingTrendResponse>> GetTrends(
        [FromQuery] Guid? bankAccountId,
        [FromQuery] string period = "monthly",
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var validPeriods = new[] { "monthly", "quarterly", "yearly" };
        if (!validPeriods.Contains(period.ToLowerInvariant()))
        {
            return BadRequest("Period must be one of: monthly, quarterly, yearly.");
        }

        var result = await _analysisService.GetSpendingTrendsAsync(
            userId.Value, bankAccountId, period, from, to, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Gets detected recurring transactions.
    /// </summary>
    [HttpGet("recurring")]
    public async Task<ActionResult<IReadOnlyList<RecurringTransactionResponse>>> GetRecurring(
        [FromQuery] Guid? bankAccountId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        // Delegate to a RecurringTransactionService via the analysis service or directly
        var result = await HttpContext.RequestServices
            .GetRequiredService<IRecurringTransactionService>()
            .GetRecurringTransactionsAsync(userId.Value, bankAccountId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Gets a cash flow forecast for the specified number of months.
    /// </summary>
    [HttpGet("forecast")]
    public async Task<ActionResult<CashflowForecastResponse>> GetForecast(
        [FromQuery] Guid? bankAccountId,
        [FromQuery] int months = 3,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await HttpContext.RequestServices
            .GetRequiredService<ICashflowForecastService>()
            .GetForecastAsync(userId.Value, bankAccountId, months, cancellationToken);
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
