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
/// Unit tests for the <see cref="TransactionsController"/>.
/// </summary>
public sealed class TransactionsControllerTests
{
    private readonly Mock<ITransactionService> _transactionServiceMock = new();
    private readonly TransactionsController _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public TransactionsControllerTests()
    {
        _sut = new TransactionsController(
            _transactionServiceMock.Object,
            Mock.Of<ILogger<TransactionsController>>());
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
    public async Task Update_WithValidRequest_ReturnsOk()
    {
        SetupUserIdentity();

        _transactionServiceMock
            .Setup(x => x.UpdateAsync(_userId, It.IsAny<Guid>(), It.IsAny<UpdateTransactionRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Update(Guid.NewGuid(), new UpdateTransactionRequest(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Update_WithNonExistentTransaction_ReturnsBadRequest()
    {
        SetupUserIdentity();

        _transactionServiceMock
            .Setup(x => x.UpdateAsync(_userId, It.IsAny<Guid>(), It.IsAny<UpdateTransactionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Transaction not found."));

        var result = await _sut.Update(Guid.NewGuid(), new UpdateTransactionRequest(), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Update_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.Update(Guid.NewGuid(), new UpdateTransactionRequest(), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
