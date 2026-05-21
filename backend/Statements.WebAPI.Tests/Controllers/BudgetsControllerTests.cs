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
/// Unit tests for the <see cref="BudgetsController"/>.
/// </summary>
public sealed class BudgetsControllerTests
{
    private readonly Mock<IBudgetService> _budgetServiceMock = new();
    private readonly BudgetsController _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public BudgetsControllerTests()
    {
        _sut = new BudgetsController(
            _budgetServiceMock.Object,
            Mock.Of<ILogger<BudgetsController>>());
    }

    private void SetupUserIdentity()
    {
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, _userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "test");
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private void SetupNoUserIdentity()
    {
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task List_WithValidUser_ReturnsOk()
    {
        SetupUserIdentity();

        _budgetServiceMock
            .Setup(x => x.ListAsync(_userId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BudgetResponse>());

        var result = await _sut.List(2026, 5, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task List_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.List(2026, 5, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task CreateOrUpdate_WithValidRequest_ReturnsOk()
    {
        SetupUserIdentity();

        _budgetServiceMock
            .Setup(x => x.CreateOrUpdateAsync(_userId, It.IsAny<CreateBudgetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetResponse());

        var result = await _sut.CreateOrUpdate(new CreateBudgetRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Delete_WithExistingBudget_ReturnsNoContent()
    {
        SetupUserIdentity();

        _budgetServiceMock
            .Setup(x => x.DeleteAsync(_userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Delete(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_WithNonExistentBudget_ReturnsNotFound()
    {
        SetupUserIdentity();

        _budgetServiceMock
            .Setup(x => x.DeleteAsync(_userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Budget not found."));

        var result = await _sut.Delete(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetProgress_ReturnsOk()
    {
        SetupUserIdentity();

        _budgetServiceMock
            .Setup(x => x.GetProgressAsync(_userId, It.IsAny<DateOnly>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BudgetProgressResponse>());

        var result = await _sut.GetProgress(2026, 5, null, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
