using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.BankAccounts;
using Statements.WebAPI.Controllers.v1;
using Statements.WebAPI.Services.BankAccounts;

namespace Statements.WebAPI.Tests.Controllers;

/// <summary>
/// Unit tests for the <see cref="BankAccountsController"/>.
/// </summary>
public sealed class BankAccountsControllerTests
{
    private readonly Mock<IBankAccountService> _bankAccountServiceMock = new();
    private readonly BankAccountsController _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public BankAccountsControllerTests()
    {
        _sut = new BankAccountsController(
            _bankAccountServiceMock.Object,
            Mock.Of<ILogger<BankAccountsController>>());
    }

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
        var accounts = new List<BankAccountResponse>
        {
            new() { Id = Guid.NewGuid(), UserId = _userId, AccountName = "Test", BankName = "", Currency = "AUD", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }
        };

        _bankAccountServiceMock
            .Setup(x => x.ListAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);

        var result = await _sut.List(CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(accounts);
    }

    [Fact]
    public async Task List_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.List(CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Create_WithValidRequest_ReturnsCreated()
    {
        SetupUserIdentity();
        var accountId = Guid.NewGuid();

        _bankAccountServiceMock
            .Setup(x => x.CreateAsync(_userId, It.IsAny<CreateBankAccountRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccountResponse
            {
                Id = accountId,
                UserId = _userId,
                AccountName = "New Account",
                BankName = "",
                Currency = "AUD",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var request = new CreateBankAccountRequest("New Account", null);
        var result = await _sut.Create(request, CancellationToken.None);

        var createdResult = result.Result.Should().BeOfType<CreatedResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Create_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.Create(null, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Update_WithValidRequest_ReturnsOk()
    {
        SetupUserIdentity();
        var accountId = Guid.NewGuid();
        var request = new UpdateBankAccountRequest("Updated", null);

        _bankAccountServiceMock
            .Setup(x => x.UpdateAsync(_userId, accountId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAccountResponse
            {
                Id = accountId,
                UserId = _userId,
                AccountName = "Updated",
                BankName = "",
                Currency = "AUD",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var result = await _sut.Update(accountId, request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Update_WithNonexistentAccount_ReturnsBadRequest()
    {
        SetupUserIdentity();
        var accountId = Guid.NewGuid();
        var request = new UpdateBankAccountRequest("Updated", null);

        _bankAccountServiceMock
            .Setup(x => x.UpdateAsync(_userId, accountId, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Bank account not found."));

        var result = await _sut.Update(accountId, request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Delete_WithValidAccount_ReturnsNoContent()
    {
        SetupUserIdentity();
        var accountId = Guid.NewGuid();

        _bankAccountServiceMock
            .Setup(x => x.DeleteAsync(_userId, accountId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Delete(accountId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_WithNonexistentAccount_ReturnsBadRequest()
    {
        SetupUserIdentity();
        var accountId = Guid.NewGuid();

        _bankAccountServiceMock
            .Setup(x => x.DeleteAsync(_userId, accountId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Bank account not found."));

        var result = await _sut.Delete(accountId, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Delete_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.Delete(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
