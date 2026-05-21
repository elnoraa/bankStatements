using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Contracts.Auth;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Tests.Services.Auth;

/// <summary>
/// Unit tests for <see cref="AuthService"/>.
/// </summary>
public sealed class AuthServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly Mock<IDbConnectionFactory> _connectionFactoryMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock = new();
    private readonly Mock<IExternalAuthValidator> _externalAuthValidatorMock = new();
    private readonly JwtOptions _jwtOptions = new()
    {
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        Secret = "ThisIsA256BitLongSecretKeyForTestingPurposes!!",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30
    };
    private readonly AuthService _sut;
    private readonly JwtAccessToken _testAccessToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthServiceTests"/> class.
    /// Sets up mocks and the system under test.
    /// </summary>
    public AuthServiceTests()
    {
        _testAccessToken = new JwtAccessToken("test-token", DateTimeOffset.UtcNow.AddMinutes(15));
        _jwtTokenServiceMock
            .Setup(x => x.CreateAccessToken(It.IsAny<AuthUser>()))
            .Returns(_testAccessToken);

        _sut = new AuthService(
            _dbExecutorMock.Object,
            _connectionFactoryMock.Object,
            _passwordHasherMock.Object,
            _jwtTokenServiceMock.Object,
            Options.Create(_jwtOptions),
            _externalAuthValidatorMock.Object,
            Mock.Of<ILogger<AuthService>>());
    }

    // ── RegisterAsync ──────────────────────────────────────────

    /// <summary>
    /// Verifies that registering a new email returns an <see cref="AuthResponse"/> with a token.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_WithNewEmail_ReturnsAuthResponse()
    {
        var request = new RegisterRequest { Email = "new@test.com", DisplayName = "New User", Password = "SecureP@ss1" };
        var userId = Guid.NewGuid();

        // No existing user found
        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<Guid?>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid?)null);

        _passwordHasherMock
            .Setup(x => x.Hash(request.Password))
            .Returns("hashed-password");

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new AuthUser
            {
                Id = userId,
                Email = "new@test.com",
                DisplayName = "New User",
                EmailVerified = false,
                PasswordHash = "hashed-password"
            });

        // CreateAuthResponseAsync calls ExecuteAsync to insert refresh token
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        var result = await _sut.RegisterAsync(request, CancellationToken.None);

        result.AccessToken.Should().Be("test-token");
        result.User.Id.Should().Be(userId);
        result.User.Email.Should().Be("new@test.com");
        result.User.DisplayName.Should().Be("New User");
    }

    /// <summary>
    /// Verifies that registering an existing email throws <see cref="AuthConflictException"/>.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ThrowsAuthConflictException()
    {
        var request = new RegisterRequest { Email = "existing@test.com", DisplayName = "Existing", Password = "SecureP@ss1" };
        var existingUserId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<Guid?>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(existingUserId);

        var act = () => _sut.RegisterAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<AuthConflictException>();
        _passwordHasherMock.Verify(x => x.Hash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WithNullDisplayName_UsesEmailPrefix()
    {
        var request = new RegisterRequest { Email = "testuser@test.com", DisplayName = null, Password = "SecureP@ss1" };
        var userId = Guid.NewGuid();

        _dbExecutorMock
            .SetupSequence(x => x.QueryFirstOrDefaultAsync<Guid?>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid?)null); // no existing user

        _passwordHasherMock
            .Setup(x => x.Hash(request.Password))
            .Returns("hashed-password");

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new AuthUser
            {
                Id = userId,
                Email = "testuser@test.com",
                DisplayName = "testuser",
                EmailVerified = false,
                PasswordHash = "hashed-password"
            });

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        var result = await _sut.RegisterAsync(request, CancellationToken.None);

        result.User.DisplayName.Should().Be("testuser");
    }

    // ── LoginAsync ─────────────────────────────────────────────

    /// <summary>
    /// Verifies that valid login credentials return an <see cref="AuthResponse"/>.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsAuthResponse()
    {
        var request = new LoginRequest { Email = "test@test.com", Password = "CorrectP@ss1" };
        var userId = Guid.NewGuid();

        var user = new AuthUser
        {
            Id = userId,
            Email = "test@test.com",
            DisplayName = "Test User",
            PasswordHash = "hashed-password",
            FailedLoginAttempts = 0,
            LockedUntil = null
        };

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(request.Password, user.PasswordHash))
            .Returns(true);

        // First ExecuteAsync = reset failed attempts
        // Second = insert refresh token (from CreateAuthResponseAsync)
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(user);

        var result = await _sut.LoginAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.User.Id.Should().Be(userId);
    }

    /// <summary>
    /// Verifies that an invalid password throws <see cref="AuthInvalidCredentialsException"/>.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ThrowsAuthInvalidCredentialsException()
    {
        var request = new LoginRequest { Email = "test@test.com", Password = "WrongP@ss1" };
        var userId = Guid.NewGuid();

        var user = new AuthUser
        {
            Id = userId,
            Email = "test@test.com",
            DisplayName = "Test User",
            PasswordHash = "hashed-password",
            FailedLoginAttempts = 0,
            LockedUntil = null
        };

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(request.Password, user.PasswordHash))
            .Returns(false);

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        var act = () => _sut.LoginAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
    }

    /// <summary>
    /// Verifies that a non-existent email throws <see cref="AuthInvalidCredentialsException"/>.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WithNonexistentEmail_ThrowsAuthInvalidCredentialsException()
    {
        var request = new LoginRequest { Email = "nonexistent@test.com", Password = "SomeP@ss1" };

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((AuthUser?)null);

        var act = () => _sut.LoginAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
    }

    /// <summary>
    /// Verifies that a locked account throws <see cref="AuthAccountLockedException"/>.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WithLockedAccount_ThrowsAuthAccountLockedException()
    {
        var request = new LoginRequest { Email = "test@test.com", Password = "AnyP@ss1" };
        var lockedUntil = DateTime.UtcNow.AddMinutes(10);

        var user = new AuthUser
        {
            Id = Guid.NewGuid(),
            Email = "test@test.com",
            DisplayName = "Test User",
            PasswordHash = "hashed-password",
            FailedLoginAttempts = 5,
            LockedUntil = lockedUntil
        };

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(user);

        var act = () => _sut.LoginAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<AuthAccountLockedException>()
            .Where(e => e.LockedUntil == lockedUntil);
        _passwordHasherMock.Verify(x => x.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a successful login resets the failed login attempts counter.
    /// </summary>
    [Fact]
    public async Task LoginAsync_OnSuccess_ResetsFailedLoginAttempts()
    {
        var request = new LoginRequest { Email = "test@test.com", Password = "CorrectP@ss1" };
        var user = new AuthUser
        {
            Id = Guid.NewGuid(),
            Email = "test@test.com",
            DisplayName = "Test User",
            PasswordHash = "hashed-password",
            FailedLoginAttempts = 0,
            LockedUntil = null
        };

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.Verify(request.Password, user.PasswordHash))
            .Returns(true);

        // ExecuteAsync called twice: reset failed attempts + insert refresh token
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(user);

        await _sut.LoginAsync(request, CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("failed_login_attempts = 0"))), Times.Once);
    }

    // ── RefreshTokenAsync ──────────────────────────────────────
    /// <summary>
    /// Verifies that a valid refresh token returns a new <see cref="AuthResponse"/>.
    /// </summary>
    [Fact]
    public async Task RefreshTokenAsync_WithValidToken_ReturnsAuthResponse()
    {
        var userId = Guid.NewGuid();
        var storedToken = (Id: Guid.NewGuid(), UserId: userId, ExpiresAt: DateTime.UtcNow.AddDays(1), RevokedAt: (DateTime?)null);

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(storedToken);

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new AuthUser
            {
                Id = userId,
                Email = "test@test.com",
                DisplayName = "Test User",
                EmailVerified = true,
                PasswordHash = "hash"
            });

        var result = await _sut.RefreshTokenAsync("valid-refresh-token", CancellationToken.None);

        result.Should().NotBeNull();
        result.User.Id.Should().Be(userId);
    }

    /// <summary>
    /// Verifies that an expired refresh token throws <see cref="AuthInvalidCredentialsException"/>.
    /// </summary>
    [Fact]
    public async Task RefreshTokenAsync_WithExpiredToken_ThrowsAuthInvalidCredentialsException()
    {
        var storedToken = (Id: Guid.NewGuid(), UserId: Guid.NewGuid(), ExpiresAt: DateTime.UtcNow.AddDays(-1), RevokedAt: (DateTime?)null);

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(storedToken);

        var act = () => _sut.RefreshTokenAsync("expired-token", CancellationToken.None);

        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
    }

    /// <summary>
    /// Verifies that a revoked refresh token throws <see cref="AuthInvalidCredentialsException"/>.
    /// </summary>
    [Fact]
    public async Task RefreshTokenAsync_WithRevokedToken_ThrowsAuthInvalidCredentialsException()
    {
        var storedToken = (Id: Guid.NewGuid(), UserId: Guid.NewGuid(), ExpiresAt: DateTime.UtcNow.AddDays(1), RevokedAt: (DateTime?)DateTime.UtcNow);

        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(storedToken);

        var act = () => _sut.RefreshTokenAsync("revoked-token", CancellationToken.None);

        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
    }

    /// <summary>
    /// Verifies that a non-existent refresh token throws <see cref="AuthInvalidCredentialsException"/>.
    /// </summary>
    [Fact]
    public async Task RefreshTokenAsync_WithNonexistentToken_ThrowsAuthInvalidCredentialsException()
    {
        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<(Guid, Guid, DateTime, DateTime?)>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid.Empty, Guid.Empty, DateTime.MinValue, (DateTime?)null));

        var act = () => _sut.RefreshTokenAsync("nonexistent-token", CancellationToken.None);

        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
    }

    // ── RevokeTokenAsync ───────────────────────────────────────
    /// <summary>
    /// Verifies that revoking a valid token executes the database update.
    /// </summary>
    [Fact]
    public async Task RevokeTokenAsync_WithValidToken_UpdatesDatabase()
    {
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.RevokeTokenAsync("valid-token", CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()), Times.Once);
    }

    // ── ExternalLoginAsync ─────────────────────────────────────
    /// <summary>
    /// Verifies that an existing external login mapping returns an <see cref="AuthResponse"/>.
    /// </summary>
    [Fact]
    public async Task ExternalLoginAsync_WithExistingExternalLogin_ReturnsAuthResponse()
    {
        var userId = Guid.NewGuid();
        var request = new ExternalLoginRequest { Provider = "google", IdToken = "valid-id-token" };

        _externalAuthValidatorMock
            .Setup(x => x.ValidateAsync("google", "valid-id-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalUserInfo("google", "provider-key-123", "test@gmail.com", "Test User", true));

        // Step 1: existing external_login mapping found
        _dbExecutorMock
            .Setup(x => x.QueryFirstOrDefaultAsync<Guid?>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(userId);

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new AuthUser
            {
                Id = userId,
                Email = "test@gmail.com",
                DisplayName = "Test User",
                EmailVerified = true,
                PasswordHash = "hash"
            });

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        var result = await _sut.ExternalLoginAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.User.Id.Should().Be(userId);
    }

    /// <summary>
    /// Verifies that an external login with an existing email but no mapping links the external account.
    /// </summary>
    [Fact]
    public async Task ExternalLoginAsync_WithExistingEmailButNoMapping_LinksAccount()
    {
        var userId = Guid.NewGuid();
        var request = new ExternalLoginRequest { Provider = "google", IdToken = "valid-id-token" };

        _externalAuthValidatorMock
            .Setup(x => x.ValidateAsync("google", "valid-id-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalUserInfo("google", "provider-key-123", "test@gmail.com", "Test User", true));

        // Step 1: no existing external_login mapping → null
        // Step 2: existing by email found → userId
        _dbExecutorMock
            .SetupSequence(x => x.QueryFirstOrDefaultAsync<Guid?>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid?)null)   // Step 1: no external mapping
            .ReturnsAsync(userId);       // Step 2: found by email

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AuthUser>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new AuthUser
            {
                Id = userId,
                Email = "test@gmail.com",
                DisplayName = "Test User",
                EmailVerified = true,
                PasswordHash = "hash"
            });

        var result = await _sut.ExternalLoginAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.User.Id.Should().Be(userId);
    }

    /// <summary>
    /// Verifies that when external auth validation fails, the exception is propagated to the caller.
    /// </summary>
    [Fact]
    public async Task ExternalLoginAsync_WhenValidationFails_PropagatesException()
    {
        var request = new ExternalLoginRequest { Provider = "google", IdToken = "invalid-token" };

        _externalAuthValidatorMock
            .Setup(x => x.ValidateAsync("google", "invalid-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid external token"));

        var act = () => _sut.ExternalLoginAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid external token");
    }
}
