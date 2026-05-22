using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Audit;

namespace Statements.WebAPI.Tests.Services.Audit;

/// <summary>
/// Unit tests for <see cref="AuditService"/>.
/// </summary>
public sealed class AuditServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly AuditService _sut;

    public AuditServiceTests()
    {
        _sut = new AuditService(_dbExecutorMock.Object, Mock.Of<ILogger<AuditService>>());
    }

    [Fact]
    public async Task LogAsync_WithValidData_InsertsAuditEntry()
    {
        var userId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.LogAsync(userId, "user.login", "user", userId, null, CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("INSERT INTO audit_log"))), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WithNullUserId_DoesNotThrow()
    {
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        var act = () => _sut.LogAsync(null, "system.action", null, null, null, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LogAsync_WhenInsertFails_DoesNotThrow()
    {
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var act = () => _sut.LogAsync(Guid.NewGuid(), "test.action", null, null, null, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LogAsync_WithDetails_SerializesToJson()
    {
        var details = new { key = "value", count = 42 };

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.LogAsync(null, "test.details", "test", null, details, CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("::jsonb"))), Times.Once);
    }
}
