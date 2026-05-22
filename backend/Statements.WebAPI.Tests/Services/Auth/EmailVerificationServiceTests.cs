using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Auth;
using Statements.WebAPI.Services.Email;

namespace Statements.WebAPI.Tests.Services.Auth;

/// <summary>
/// Unit tests for <see cref="EmailVerificationService"/>.
/// </summary>
public sealed class EmailVerificationServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly EmailVerificationService _sut;

    public EmailVerificationServiceTests()
    {
        _sut = new EmailVerificationService(
            _dbExecutorMock.Object,
            _emailServiceMock.Object,
            _passwordHasherMock.Object,
            Mock.Of<ILogger<EmailVerificationService>>());
    }

    [Fact]
    public async Task SendVerificationEmailAsync_InsertsTokenAndSendsEmail()
    {
        var userId = Guid.NewGuid();
        const string email = "test@example.com";

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.SendVerificationEmailAsync(userId, email, CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("INSERT INTO email_tokens"))), Times.Once);
        _emailServiceMock.Verify(x => x.SendAsync(email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyEmailAsync_WithValidToken_MarksUserVerified()
    {
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        const string token = "valid-token";

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((tokenId, userId, DateTime.UtcNow.AddHours(1), (DateTime?)null));

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.VerifyEmailAsync(token, CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("UPDATE app_users SET email_verified = TRUE"))), Times.Once);
    }

    [Fact]
    public async Task VerifyEmailAsync_WithExpiredToken_ThrowsInvalidOperationException()
    {
        const string token = "expired-token";

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), (DateTime?)null));

        var act = () => _sut.VerifyEmailAsync(token, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*expired*");
    }

    [Fact]
    public async Task VerifyEmailAsync_WithAlreadyUsedToken_ThrowsInvalidOperationException()
    {
        const string token = "used-token";

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddHours(1), DateTime.UtcNow));

        var act = () => _sut.VerifyEmailAsync(token, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been used*");
    }

    [Fact]
    public async Task VerifyEmailAsync_WithNonexistentToken_ThrowsInvalidOperationException()
    {
        const string token = "nonexistent-token";

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid.Empty, Guid.Empty, DateTime.MinValue, (DateTime?)null));

        var act = () => _sut.VerifyEmailAsync(token, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Invalid*");
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_WithExistingUser_SendsEmail()
    {
        var userId = Guid.NewGuid();
        const string email = "test@example.com";

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new AuthUser { Id = userId, Email = email, DisplayName = "Test" });

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.SendPasswordResetEmailAsync(email, CancellationToken.None);

        _emailServiceMock.Verify(x => x.SendAsync(email, It.Is<string>(s => s.Contains("reset", StringComparison.OrdinalIgnoreCase)), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_WithNonExistentUser_DoesNotSendEmail()
    {
        const string email = "nonexistent@example.com";

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((AuthUser?)null);

        await _sut.SendPasswordResetEmailAsync(email, CancellationToken.None);

        _emailServiceMock.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithValidToken_UpdatesPassword()
    {
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        const string token = "valid-reset-token";
        const string newPassword = "NewSecureP@ss1";

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((tokenId, userId, DateTime.UtcNow.AddMinutes(10), (DateTime?)null));

        _passwordHasherMock
            .Setup(x => x.Hash(newPassword))
            .Returns("new-hashed-password");

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.ResetPasswordAsync(token, newPassword, CancellationToken.None);

        _passwordHasherMock.Verify(x => x.Hash(newPassword), Times.Once);
        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("UPDATE app_users SET password_hash"))), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithExpiredToken_ThrowsInvalidOperationException()
    {
        const string token = "expired-reset-token";

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1), (DateTime?)null));

        var act = () => _sut.ResetPasswordAsync(token, "NewP@ss1", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*expired*");
    }
}
