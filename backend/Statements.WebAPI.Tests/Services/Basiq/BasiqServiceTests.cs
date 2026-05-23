using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Statements.WebAPI.Contracts.Basiq;
using Statements.WebAPI.Data;
using Statements.WebAPI.Hubs;

namespace Statements.WebAPI.Services.Basiq.Tests;

public sealed class BasiqServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly Mock<IBasiqApiClient> _apiClientMock = new();
    private readonly Mock<IHubContext<StatementProcessingHub>> _hubContextMock = new();
    private readonly BasiqService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public BasiqServiceTests()
    {
        var options = new BasiqOptions
        {
            ApiKey = "test-key",
            ConsentRedirectUrl = "http://localhost:5213/api/v1/basiq/connections/callback"
        };

        _sut = new BasiqService(
            _dbExecutorMock.Object,
            _apiClientMock.Object,
            _hubContextMock.Object,
            Options.Create(options),
            Mock.Of<ILogger<BasiqService>>());
    }

    [Fact]
    public async Task InitiateConnectionAsync_NoExistingUser_CreatesBasiqUser()
    {
        var email = "test@example.com";
        var basiqUserId = Guid.NewGuid().ToString();

        // User email lookup
        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(email);

        // No existing Basiq user ID
        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.Is<CommandDefinition>(cd =>
                cd.CommandText.Contains("basiq_user_id"))))
            .ReturnsAsync((string?)null);

        // Create Basiq user
        _apiClientMock
            .Setup(x => x.CreateUserAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(basiqUserId);

        // Generate client token
        _apiClientMock
            .Setup(x => x.GenerateClientTokenAsync(basiqUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-token-123");

        // Insert pending connection
        var response = new BasiqConnectionResponse
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            InstitutionName = "ANZ",
            Status = "pending",
            SyncEnabled = true,
            SyncFrequencyMinutes = 1440
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<BasiqConnectionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(response);

        var result = await _sut.InitiateConnectionAsync(
            _userId,
            new InitiateConnectionRequest { InstitutionName = "ANZ" },
            CancellationToken.None);

        result.Should().NotBeNull();
        result.ConnectionId.Should().Be(response.Id);
        result.ConsentUrl.Should().Contain("client-token-123");
        result.Status.Should().Be("pending");

        _apiClientMock.Verify(x => x.CreateUserAsync(email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateConnectionAsync_ExistingUser_ReusesBasiqUserId()
    {
        var email = "test@example.com";
        var existingBasiqId = "existing-user-id";

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(email);

        // Existing Basiq user ID
        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.Is<CommandDefinition>(cd =>
                cd.CommandText.Contains("basiq_user_id"))))
            .ReturnsAsync(existingBasiqId);

        _apiClientMock
            .Setup(x => x.GenerateClientTokenAsync(existingBasiqId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-token");

        var response = new BasiqConnectionResponse
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            InstitutionName = "CBA",
            Status = "pending",
            SyncEnabled = true,
            SyncFrequencyMinutes = 1440
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<BasiqConnectionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(response);

        var result = await _sut.InitiateConnectionAsync(
            _userId,
            new InitiateConnectionRequest { InstitutionName = "CBA" },
            CancellationToken.None);

        result.Should().NotBeNull();

        // Should NOT create new Basiq user
        _apiClientMock.Verify(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitiateConnectionAsync_WithFallbackEmail_UsesSyntheticEmail()
    {
        var invalidEmail = "google-12345@noemail.local";
        var basiqUserId = Guid.NewGuid().ToString();

        // Email lookup returns the fallback email (now RFC-compliant with hyphen)
        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(invalidEmail);

        // No existing Basiq user ID
        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.Is<CommandDefinition>(cd =>
                cd.CommandText.Contains("basiq_user_id"))))
            .ReturnsAsync((string?)null);

        // CreateUserAsync should be called with the RFC-compliant email directly
        _apiClientMock
            .Setup(x => x.CreateUserAsync(
                It.Is<string>(e => e == "google-12345@noemail.local"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(basiqUserId);

        _apiClientMock
            .Setup(x => x.GenerateClientTokenAsync(basiqUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-token");

        var response = new BasiqConnectionResponse
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            InstitutionName = "ANZ",
            Status = "pending",
            SyncEnabled = true,
            SyncFrequencyMinutes = 1440
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<BasiqConnectionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(response);

        var result = await _sut.InitiateConnectionAsync(
            _userId,
            new InitiateConnectionRequest { InstitutionName = "ANZ" },
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().Be("pending");

        // Verify CreateUserAsync was called ONCE with the RFC-compliant fallback email
        _apiClientMock.Verify(
            x => x.CreateUserAsync(
                It.Is<string>(e => e == "google-12345@noemail.local"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetConnectionsAsync_WithConnections_ReturnsList()
    {
        var connections = new List<BasiqConnectionResponse>
        {
            new() { Id = Guid.NewGuid(), UserId = _userId, InstitutionName = "ANZ", Status = "active", SyncEnabled = true, SyncFrequencyMinutes = 1440 },
            new() { Id = Guid.NewGuid(), UserId = _userId, InstitutionName = "CBA", Status = "active", SyncEnabled = false, SyncFrequencyMinutes = 1440 }
        };

        _dbExecutorMock
            .Setup(x => x.QueryAsync<BasiqConnectionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(connections);

        var result = await _sut.GetConnectionsAsync(_userId, CancellationToken.None);

        result.Connections.Should().HaveCount(2);
        result.Connections[0].InstitutionName.Should().Be("ANZ");
        result.Connections[1].InstitutionName.Should().Be("CBA");
    }

    [Fact]
    public async Task GetConnectionsAsync_WithNoConnections_ReturnsEmptyList()
    {
        _dbExecutorMock
            .Setup(x => x.QueryAsync<BasiqConnectionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<BasiqConnectionResponse>());

        var result = await _sut.GetConnectionsAsync(_userId, CancellationToken.None);

        result.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConnectionAsync_WithValidId_ReturnsConnection()
    {
        var connectionId = Guid.NewGuid();
        var connection = new BasiqConnectionResponse
        {
            Id = connectionId,
            UserId = _userId,
            InstitutionName = "ANZ",
            Status = "active",
            SyncEnabled = true,
            SyncFrequencyMinutes = 1440
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<BasiqConnectionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(connection);

        var result = await _sut.GetConnectionAsync(_userId, connectionId, CancellationToken.None);

        result.Id.Should().Be(connectionId);
        result.InstitutionName.Should().Be("ANZ");
    }

    [Fact]
    public async Task GetConnectionAsync_WithInvalidId_ThrowsInvalidOperationException()
    {
        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<BasiqConnectionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((BasiqConnectionResponse?)null);

        var act = () => _sut.GetConnectionAsync(_userId, Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Connection not found.");
    }

    [Fact]
    public async Task UpdateSyncConfigAsync_WithValidRequest_UpdatesAndReturns()
    {
        var connectionId = Guid.NewGuid();
        var updated = new BasiqConnectionResponse
        {
            Id = connectionId,
            UserId = _userId,
            InstitutionName = "ANZ",
            Status = "active",
            SyncEnabled = false,
            SyncFrequencyMinutes = 10080
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<BasiqConnectionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(updated);

        var result = await _sut.UpdateSyncConfigAsync(
            _userId, connectionId,
            new UpdateSyncConfigRequest { SyncEnabled = false, SyncFrequencyMinutes = 10080 },
            CancellationToken.None);

        result.SyncEnabled.Should().BeFalse();
        result.SyncFrequencyMinutes.Should().Be(10080);
    }

    [Fact]
    public async Task RemoveConnectionAsync_WithValidId_Deletes()
    {
        var connectionId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.RemoveConnectionAsync(_userId, connectionId, CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()), Times.Once);
    }

    [Fact]
    public async Task RemoveConnectionAsync_WithInvalidId_ThrowsInvalidOperationException()
    {
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(0);

        var act = () => _sut.RemoveConnectionAsync(_userId, Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Connection not found.");
    }

    [Fact]
    public async Task GetSyncLogAsync_WithValidConnection_ReturnsLogs()
    {
        var connectionId = Guid.NewGuid();
        var logs = new List<SyncLogResponse>
        {
            new() { Id = Guid.NewGuid(), Status = "success", TransactionsFetched = 10, TransactionsInserted = 5, SyncedAt = DateTimeOffset.UtcNow }
        };

        // Verify ownership
        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<bool>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(true);

        _dbExecutorMock
            .Setup(x => x.QueryAsync<SyncLogResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(logs);

        var result = await _sut.GetSyncLogAsync(_userId, connectionId, 20, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Status.Should().Be("success");
    }
}
