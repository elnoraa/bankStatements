using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Basiq;
using Statements.WebAPI.Services.Basiq;

namespace Statements.WebAPI.Tests.Controllers;

public sealed class BasiqControllerTests
{
    private readonly Mock<IBasiqService> _basiqServiceMock = new();
    private readonly BasiqController _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public BasiqControllerTests()
    {
        _sut = new BasiqController(
            _basiqServiceMock.Object,
            Mock.Of<ILogger<BasiqController>>());
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
    public async Task InitiateConnection_WithAuth_ReturnsOk()
    {
        SetupUserIdentity();
        var request = new InitiateConnectionRequest { InstitutionName = "ANZ" };

        _basiqServiceMock
            .Setup(x => x.InitiateConnectionAsync(_userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateConnectionResponse
            {
                ConnectionId = Guid.NewGuid(),
                ConsentUrl = "https://consent.basiq.io/home?token=abc",
                InstitutionName = "ANZ",
                Status = "pending"
            });

        var result = await _sut.InitiateConnection(request, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<InitiateConnectionResponse>().Subject;
        response.InstitutionName.Should().Be("ANZ");
        response.ConsentUrl.Should().Contain("consent.basiq.io");
    }

    [Fact]
    public async Task InitiateConnection_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.InitiateConnection(
            new InitiateConnectionRequest { InstitutionName = "ANZ" },
            CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task InitiateConnection_WithEmptyInstitutionName_ReturnsBadRequest()
    {
        SetupUserIdentity();

        var result = await _sut.InitiateConnection(
            new InitiateConnectionRequest { InstitutionName = "" },
            CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListConnections_WithAuth_ReturnsOk()
    {
        SetupUserIdentity();
        var connections = new BasiqConnectionListResponse
        {
            Connections = new List<BasiqConnectionResponse>
            {
                new() { Id = Guid.NewGuid(), UserId = _userId, InstitutionName = "ANZ", Status = "active", SyncEnabled = true, SyncFrequencyMinutes = 1440 }
            }
        };

        _basiqServiceMock
            .Setup(x => x.GetConnectionsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connections);

        var result = await _sut.ListConnections(CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(connections);
    }

    [Fact]
    public async Task ListConnections_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.ListConnections(CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetConnection_WithValidId_ReturnsOk()
    {
        SetupUserIdentity();
        var connectionId = Guid.NewGuid();

        _basiqServiceMock
            .Setup(x => x.GetConnectionAsync(_userId, connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasiqConnectionResponse
            {
                Id = connectionId,
                UserId = _userId,
                InstitutionName = "ANZ",
                Status = "active",
                SyncEnabled = true,
                SyncFrequencyMinutes = 1440
            });

        var result = await _sut.GetConnection(connectionId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetConnection_WithInvalidId_ReturnsNotFound()
    {
        SetupUserIdentity();
        var connectionId = Guid.NewGuid();

        _basiqServiceMock
            .Setup(x => x.GetConnectionAsync(_userId, connectionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection not found."));

        var result = await _sut.GetConnection(connectionId, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetConnection_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.GetConnection(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task UpdateSyncConfig_WithValidRequest_ReturnsOk()
    {
        SetupUserIdentity();
        var connectionId = Guid.NewGuid();
        var request = new UpdateSyncConfigRequest { SyncEnabled = false, SyncFrequencyMinutes = 10080 };

        _basiqServiceMock
            .Setup(x => x.UpdateSyncConfigAsync(_userId, connectionId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasiqConnectionResponse
            {
                Id = connectionId,
                UserId = _userId,
                InstitutionName = "ANZ",
                Status = "active",
                SyncEnabled = false,
                SyncFrequencyMinutes = 10080
            });

        var result = await _sut.UpdateSyncConfig(connectionId, request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RefreshConnection_WithValidId_ReturnsOk()
    {
        SetupUserIdentity();
        var connectionId = Guid.NewGuid();

        _basiqServiceMock
            .Setup(x => x.RefreshConnectionAsync(_userId, connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasiqConnectionResponse
            {
                Id = connectionId,
                UserId = _userId,
                InstitutionName = "ANZ",
                Status = "active",
                SyncEnabled = true,
                SyncFrequencyMinutes = 1440
            });

        var result = await _sut.RefreshConnection(connectionId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RefreshConnection_WithInvalidId_ReturnsBadRequest()
    {
        SetupUserIdentity();
        var connectionId = Guid.NewGuid();

        _basiqServiceMock
            .Setup(x => x.RefreshConnectionAsync(_userId, connectionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection not found."));

        var result = await _sut.RefreshConnection(connectionId, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RemoveConnection_WithValidId_ReturnsNoContent()
    {
        SetupUserIdentity();
        var connectionId = Guid.NewGuid();

        _basiqServiceMock
            .Setup(x => x.RemoveConnectionAsync(_userId, connectionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.RemoveConnection(connectionId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task RemoveConnection_WithInvalidId_ReturnsBadRequest()
    {
        SetupUserIdentity();
        var connectionId = Guid.NewGuid();

        _basiqServiceMock
            .Setup(x => x.RemoveConnectionAsync(_userId, connectionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection not found."));

        var result = await _sut.RemoveConnection(connectionId, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RemoveConnection_WithoutAuth_ReturnsUnauthorized()
    {
        SetupNoUserIdentity();

        var result = await _sut.RemoveConnection(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetSyncLog_WithValidId_ReturnsOk()
    {
        SetupUserIdentity();
        var connectionId = Guid.NewGuid();
        var logs = new List<SyncLogResponse>
        {
            new() { Id = Guid.NewGuid(), Status = "success", TransactionsFetched = 10, TransactionsInserted = 5, SyncedAt = DateTimeOffset.UtcNow }
        };

        _basiqServiceMock
            .Setup(x => x.GetSyncLogAsync(_userId, connectionId, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);

        var result = await _sut.GetSyncLog(connectionId, 20, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(logs);
    }

    [Fact]
    public async Task GetSyncLog_WithInvalidId_ReturnsNotFound()
    {
        SetupUserIdentity();
        var connectionId = Guid.NewGuid();

        _basiqServiceMock
            .Setup(x => x.GetSyncLogAsync(_userId, connectionId, 20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection not found."));

        var result = await _sut.GetSyncLog(connectionId, 20, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CompleteConnection_WithValidRequest_ReturnsOk()
    {
        SetupUserIdentity();
        var request = new CompleteConnectionRequest
        {
            ConnectionId = Guid.NewGuid(),
            JobId = "job-123"
        };

        _basiqServiceMock
            .Setup(x => x.CompleteConnectionAsync(_userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasiqConnectionResponse
            {
                Id = request.ConnectionId,
                UserId = _userId,
                InstitutionName = "ANZ",
                Status = "active",
                SyncEnabled = true,
                SyncFrequencyMinutes = 1440
            });

        var result = await _sut.CompleteConnection(request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CompleteConnection_WithEmptyJobId_ReturnsBadRequest()
    {
        SetupUserIdentity();

        var result = await _sut.CompleteConnection(
            new CompleteConnectionRequest { ConnectionId = Guid.NewGuid(), JobId = "" },
            CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
