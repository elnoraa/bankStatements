using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Controllers.v1;
using Statements.WebAPI.Services.Analysis;

namespace Statements.WebAPI.Tests.Controllers;

/// <summary>
/// Unit tests for the <see cref="AnalysisController"/>.
/// </summary>
public sealed class AnalysisControllerTests
{
    private readonly Mock<IAnalysisService> _analysisServiceMock = new();
    private readonly AnalysisController _sut;
    private readonly Guid _userId = Guid.NewGuid();

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisControllerTests"/> class.
    /// </summary>
    public AnalysisControllerTests()
    {
        _sut = new AnalysisController(
            _analysisServiceMock.Object,
            Mock.Of<ILogger<AnalysisController>>());
    }

    /// <summary>
    /// Sets up the controller context with a valid user identity.
    /// </summary>
    private void SetupUserIdentity()
    {
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, _userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "test");
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    /// <summary>
    /// Sets up the controller context with no user identity (unauthenticated).
    /// </summary>
    private void SetupNoUserIdentity()
    {
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    /// <summary>
    /// Verifies that a valid user gets a 200 OK response with a spending summary.
    /// </summary>
    [Fact]
    public async Task GetSummary_WithValidUser_ReturnsOk()
    {
        SetupUserIdentity();

        _analysisServiceMock
            .Setup(x => x.GetSummaryAsync(_userId, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpendingSummaryResponse(
                new DateOnly(2025, 1, 1),
                new DateOnly(2025, 12, 31),
                50000m, 35000m, 15000m, true,
                new List<CategorySpendingResponse>(),
                new List<RecentTransactionResponse>()));

        var result = await _sut.GetSummary(null, null, null, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    /// <summary>
    /// Verifies that an invalid or missing user ID in the token returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task GetSummary_WithInvalidUserIdInToken_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.GetSummary(null, null, null, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    /// <summary>
    /// Verifies that requesting a summary with from &gt; to returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task GetSummary_WithInvalidDateRange_ReturnsBadRequest()
    {
        SetupUserIdentity();
        var from = new DateOnly(2025, 6, 1);
        var to = new DateOnly(2025, 1, 1);

        var result = await _sut.GetSummary(null, from, to, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _analysisServiceMock.Verify(
            x => x.GetSummaryAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that valid filter parameters (bank account, date range) return 200 OK.
    /// </summary>
    [Fact]
    public async Task GetSummary_WithValidFilters_ReturnsOk()
    {
        SetupUserIdentity();
        var bankAccountId = Guid.NewGuid();
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2025, 3, 31);

        _analysisServiceMock
            .Setup(x => x.GetSummaryAsync(_userId, bankAccountId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpendingSummaryResponse(
                from, to, 10000m, 5000m, 5000m, true,
                new List<CategorySpendingResponse>(),
                new List<RecentTransactionResponse>()));

        var result = await _sut.GetSummary(bankAccountId, from, to, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
