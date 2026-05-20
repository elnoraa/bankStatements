using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Statements;
using Statements.WebAPI.Controllers;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Tests.Controllers;

/// <summary>
/// Unit tests for the <see cref="StatementsController"/>.
/// </summary>
public sealed class StatementsControllerTests
{
    private readonly Mock<IStatementService> _statementServiceMock = new();
    private readonly StatementsController _sut;
    private readonly Guid _userId = Guid.NewGuid();

    /// <summary>
    /// Initializes a new instance of the <see cref="StatementsControllerTests"/> class.
    /// </summary>
    public StatementsControllerTests()
    {
        _sut = new StatementsController(
            _statementServiceMock.Object,
            Mock.Of<ILogger<StatementsController>>());
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
    /// Creates a mock PDF form file for testing.
    /// </summary>
    /// <returns>A configured mock <see cref="IFormFile"/>.</returns>
    private static Mock<IFormFile> CreateMockPdfFile()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(x => x.FileName).Returns("statement.pdf");
        fileMock.Setup(x => x.Length).Returns(1024);
        fileMock.Setup(x => x.ContentType).Returns("application/pdf");
        return fileMock;
    }

    /// <summary>
    /// Verifies that uploading with no file returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task Upload_WithNoFile_ReturnsBadRequest()
    {
        SetupUserIdentity();

        var result = await _sut.Upload(null, null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// Verifies that uploading with a null file returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task Upload_WithNullFile_ReturnsBadRequest()
    {
        SetupUserIdentity();

        var result = await _sut.Upload(null, null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// Verifies that uploading with an invalid user ID in the token returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task Upload_WithInvalidUserIdInToken_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.Upload(CreateMockPdfFile().Object, null, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    /// <summary>
    /// Verifies that a valid file upload returns 201 Created with a statement upload response.
    /// </summary>
    [Fact]
    public async Task Upload_WithValidFile_ReturnsCreated()
    {
        SetupUserIdentity();
        var file = CreateMockPdfFile().Object;
        var bankAccountId = Guid.NewGuid();

        _statementServiceMock
            .Setup(x => x.UploadAsync(_userId, bankAccountId, file, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatementUploadResponse
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                BankAccountId = bankAccountId,
                OriginalFileName = "statement.pdf",
                StoredFileName = "statement-stored.pdf",
                FileHash = "HASH123",
                SizeInBytes = 1024,
                ContentType = "application/pdf",
                Status = "processed",
                UploadedAt = DateTimeOffset.UtcNow,
                ParsedTransactionCount = 5
            });

        var result = await _sut.Upload(file, bankAccountId, CancellationToken.None);

        var createdResult = result.Result.Should().BeOfType<CreatedResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
    }

    /// <summary>
    /// Verifies that when the service throws an <see cref="InvalidOperationException"/>, the controller returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task Upload_WhenServiceThrowsInvalidOperation_ReturnsBadRequest()
    {
        SetupUserIdentity();
        var file = CreateMockPdfFile().Object;

        _statementServiceMock
            .Setup(x => x.UploadAsync(_userId, null, file, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Upload error"));

        var result = await _sut.Upload(file, null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
