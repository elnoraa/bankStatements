using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Statements;
using Statements.WebAPI.Controllers.v1;
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

        var result = await _sut.Upload(null, Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// Verifies that uploading with a null file returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task Upload_WithNullFile_ReturnsBadRequest()
    {
        SetupUserIdentity();

        var result = await _sut.Upload(null, Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// Verifies that uploading with an invalid user ID in the token returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task Upload_WithInvalidUserIdInToken_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.Upload(CreateMockPdfFile().Object, Guid.NewGuid(), CancellationToken.None);

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
                Status = "uploaded",
                UploadedAt = DateTimeOffset.UtcNow,
                ParsedTransactionCount = 0
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
        var bankAccountId = Guid.NewGuid();

        _statementServiceMock
            .Setup(x => x.UploadAsync(_userId, bankAccountId, file, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Upload error"));

        var result = await _sut.Upload(file, bankAccountId, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- GET /api/v1/statements/{statementId} tests ---

    /// <summary>
    /// Verifies that GetStatement with a valid ID returns 200 OK.
    /// </summary>
    [Fact]
    public async Task GetStatement_WithValidId_ReturnsOk()
    {
        SetupUserIdentity();
        var statementId = Guid.NewGuid();

        _statementServiceMock
            .Setup(x => x.GetStatementAsync(_userId, statementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatementUploadResponse
            {
                Id = statementId,
                UserId = _userId,
                OriginalFileName = "statement.pdf",
                StoredFileName = "statement-file.pdf",
                FileHash = "HASH123",
                SizeInBytes = 1024,
                Status = "processing",
                UploadedAt = DateTimeOffset.UtcNow
            });

        var result = await _sut.GetStatement(statementId, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    /// <summary>
    /// Verifies that GetStatement with a non-existent ID returns 404 NotFound.
    /// </summary>
    [Fact]
    public async Task GetStatement_WithNonExistentId_ReturnsNotFound()
    {
        SetupUserIdentity();

        _statementServiceMock
            .Setup(x => x.GetStatementAsync(_userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatementUploadResponse?)null);

        var result = await _sut.GetStatement(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    /// <summary>
    /// Verifies that GetStatement without authentication returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task GetStatement_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.GetStatement(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // --- GET /api/v1/statements (List) tests ---

    [Fact]
    public async Task List_WithValidUser_ReturnsOk()
    {
        SetupUserIdentity();

        _statementServiceMock
            .Setup(x => x.ListAsync(_userId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StatementListItemResponse>());

        var result = await _sut.List(1, 20, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task List_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.List(1, 20, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // --- POST /api/v1/statements/{id}/retry tests ---

    [Fact]
    public async Task Retry_WithFailedStatement_ReturnsOk()
    {
        SetupUserIdentity();
        var statementId = Guid.NewGuid();

        _statementServiceMock
            .Setup(x => x.RetryAsync(_userId, statementId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Retry(statementId, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Retry_WithNonFailedStatement_ReturnsBadRequest()
    {
        SetupUserIdentity();
        var statementId = Guid.NewGuid();

        _statementServiceMock
            .Setup(x => x.RetryAsync(_userId, statementId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Only failed statements can be retried."));

        var result = await _sut.Retry(statementId, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Retry_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.Retry(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
