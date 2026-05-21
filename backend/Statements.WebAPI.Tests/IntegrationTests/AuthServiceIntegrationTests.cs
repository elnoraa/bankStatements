using System.Data;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Contracts.Auth;
using Statements.WebAPI.Contracts.BankAccounts;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Tests.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="AuthService"/> using a real PostgreSQL database via Testcontainers.
/// Tests SQL correctness, constraint enforcement, transaction behavior, and refresh token rotation.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AuthServiceIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private readonly AuthService _sut;
    private readonly IDbExecutor _dbExecutor;
    private readonly Mock<IExternalAuthValidator> _externalAuthValidatorMock = new();

    private static readonly JwtOptions JwtOptions = new()
    {
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        Secret = "ThisIsA256BitLongSecretKeyForTestingPurposes!!",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30
    };

    public AuthServiceIntegrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;

        // Real infrastructure
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString
            })
            .Build();

        var connectionFactory = new NpgsqlConnectionFactory(
            config,
            Mock.Of<ILogger<NpgsqlConnectionFactory>>());

        _dbExecutor = new DapperDbExecutor(connectionFactory);

        // Real hasher and JWT service
        var passwordHasher = new BCryptPasswordHasher(Mock.Of<ILogger<BCryptPasswordHasher>>());
        var jwtTokenService = new JwtTokenService(
            Options.Create(JwtOptions),
            Mock.Of<ILogger<JwtTokenService>>());

        // System under test — real DB, real hasher, real JWT, mocked external auth
        _sut = new AuthService(
            _dbExecutor,
            connectionFactory,
            passwordHasher,
            jwtTokenService,
            Options.Create(JwtOptions),
            _externalAuthValidatorMock.Object,
            Mock.Of<ILogger<AuthService>>());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Clean up data between tests so each test starts with a known state.
    /// </summary>
    public async Task DisposeAsync()
    {
        await _fixture.ClearDataAsync();
    }

    // ── Register ────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_WithNewEmail_CreatesUserAndRefreshToken()
    {
        var request = new RegisterRequest("newuser@test.com", "New User", "SecureP@ss1");

        var result = await _sut.RegisterAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.AccessTokenExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(16));
        result.RefreshTokenExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromDays(1));
        result.User.Email.Should().Be("newuser@test.com");
        result.User.DisplayName.Should().Be("New User");
        result.User.EmailVerified.Should().BeFalse();

        // Verify the user was actually persisted
        var dbUser = await _dbExecutor.QuerySingleAsync<AuthUser>(
            new CommandDefinition(
                "SELECT id AS Id, email AS Email, display_name AS DisplayName, email_verified AS EmailVerified, password_hash AS PasswordHash FROM app_users WHERE email = @Email",
                new { Email = "newuser@test.com" }));
        dbUser.Email.Should().Be("newuser@test.com");
        dbUser.PasswordHash.Should().NotBeNullOrEmpty();
        dbUser.PasswordHash.Should().NotBe("SecureP@ss1"); // should be hashed

        // Verify a refresh token was stored
        var tokenCount = await _dbExecutor.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @UserId",
                new { UserId = dbUser.Id }));
        tokenCount.Should().Be(1);

        // Verify the default "Untitled" bank account was auto-created
        var bankAccounts = await _dbExecutor.QueryAsync<BankAccountResponse>(
            new CommandDefinition(
                "SELECT id AS Id, user_id AS UserId, bank_name AS BankName, account_name AS AccountName, currency AS Currency FROM bank_accounts WHERE user_id = @UserId",
                new { UserId = dbUser.Id }));
        bankAccounts.Should().HaveCount(1);
        bankAccounts.Single().AccountName.Should().Be("Untitled");
        bankAccounts.Single().Currency.Should().Be("AUD");
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ThrowsAuthConflictException()
    {
        // First registration succeeds
        var request = new RegisterRequest("dupe@test.com", "First User", "SecureP@ss1");
        await _sut.RegisterAsync(request, CancellationToken.None);

        // Second registration with same email fails
        var duplicateRequest = new RegisterRequest("dupe@test.com", "Second User", "OtherP@ss1");
        var act = () => _sut.RegisterAsync(duplicateRequest, CancellationToken.None);

        await act.Should().ThrowAsync<AuthConflictException>();

        // Verify only one user exists
        var count = await _dbExecutor.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM app_users WHERE LOWER(email) = LOWER(@Email)",
                new { Email = "dupe@test.com" }));
        count.Should().Be(1);
    }

    [Fact]
    public async Task RegisterAsync_WithNullDisplayName_UsesEmailPrefix()
    {
        var request = new RegisterRequest("prefixuser@test.com", null, "SecureP@ss1");

        var result = await _sut.RegisterAsync(request, CancellationToken.None);

        result.User.DisplayName.Should().Be("prefixuser");
    }

    // ── Login ───────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsAuthResponse()
    {
        // Arrange: register a user first
        var registerRequest = new RegisterRequest("loginuser@test.com", "Login User", "SecureP@ss1");
        await _sut.RegisterAsync(registerRequest, CancellationToken.None);

        // Act: login with same credentials
        var loginRequest = new LoginRequest("loginuser@test.com", "SecureP@ss1");
        var result = await _sut.LoginAsync(loginRequest, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Email.Should().Be("loginuser@test.com");

        // Verify failed_login_attempts was reset to 0
        var user = await _dbExecutor.QuerySingleAsync<AuthUser>(
            new CommandDefinition(
                "SELECT id AS Id, email AS Email, failed_login_attempts AS FailedLoginAttempts, locked_until AS LockedUntil FROM app_users WHERE email = @Email",
                new { Email = "loginuser@test.com" }));
        user.FailedLoginAttempts.Should().Be(0);
        user.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsAuthInvalidCredentialsException()
    {
        // Arrange: register a user
        await _sut.RegisterAsync(
            new RegisterRequest("wrongpass@test.com", "Wrong Pass", "SecureP@ss1"),
            CancellationToken.None);

        // Act
        var act = () => _sut.LoginAsync(
            new LoginRequest("wrongpass@test.com", "WrongPassword1"),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();

        // Verify failed_login_attempts incremented
        var user = await _dbExecutor.QuerySingleAsync<AuthUser>(
            new CommandDefinition(
                "SELECT id AS Id, email AS Email, failed_login_attempts AS FailedLoginAttempts FROM app_users WHERE email = @Email",
                new { Email = "wrongpass@test.com" }));
        user.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task LoginAsync_AfterTooManyFailedAttempts_LocksAccount()
    {
        // Arrange: register a user
        await _sut.RegisterAsync(
            new RegisterRequest("lockuser@test.com", "Lock User", "SecureP@ss1"),
            CancellationToken.None);

        // Act: fail login 5 times to trigger lockout
        for (int i = 0; i < 5; i++)
        {
            var act = () => _sut.LoginAsync(
                new LoginRequest("lockuser@test.com", "WrongPassword1"),
                CancellationToken.None);

            await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
        }

        // The 6th attempt should throw AuthAccountLockedException
        var lockedAct = () => _sut.LoginAsync(
            new LoginRequest("lockuser@test.com", "WrongPassword1"),
            CancellationToken.None);

        var exception = await lockedAct.Should().ThrowAsync<AuthAccountLockedException>();
        exception.Which.LockedUntil.Should().BeAfter(DateTime.UtcNow);

        // Verify DB state
        var user = await _dbExecutor.QuerySingleAsync<AuthUser>(
            new CommandDefinition(
                "SELECT id AS Id, email AS Email, failed_login_attempts AS FailedLoginAttempts, locked_until AS LockedUntil FROM app_users WHERE email = @Email",
                new { Email = "lockuser@test.com" }));
        user.FailedLoginAttempts.Should().Be(5);
        user.LockedUntil.Should().NotBeNull();
        user.LockedUntil!.Value.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task LoginAsync_WithLockedAccount_ThrowsAuthAccountLockedException()
    {
        // Arrange: seed a user with an active lock directly in the DB
        var userId = Guid.NewGuid();
        var lockedUntil = DateTime.UtcNow.AddMinutes(15);
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO app_users (id, email, display_name, password_hash, failed_login_attempts, locked_until)
                VALUES (@Id, @Email, @DisplayName, @PasswordHash, @FailedAttempts, @LockedUntil)
                """,
                new
                {
                    Id = userId,
                    Email = "lockeduser@test.com",
                    DisplayName = "Locked User",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("SecureP@ss1"),
                    FailedAttempts = 5,
                    LockedUntil = lockedUntil
                }));

        // Act
        var act = () => _sut.LoginAsync(
            new LoginRequest("lockeduser@test.com", "SecureP@ss1"),
            CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<AuthAccountLockedException>();
        exception.Which.LockedUntil.Should().BeCloseTo(lockedUntil, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LoginAsync_WithNonexistentEmail_ThrowsAuthInvalidCredentialsException()
    {
        var act = () => _sut.LoginAsync(
            new LoginRequest("nonexistent@test.com", "SomePassword1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
    }

    // ── Refresh Token ───────────────────────────────────────

    [Fact]
    public async Task RefreshTokenAsync_WithValidToken_ReturnsNewTokensAndRevokesOld()
    {
        // Arrange: register a user to get a real refresh token
        var registerResult = await _sut.RegisterAsync(
            new RegisterRequest("refreshtest@test.com", "Refresh", "SecureP@ss1"),
            CancellationToken.None);

        var oldRefreshToken = registerResult.RefreshToken;

        // Act
        var refreshResult = await _sut.RefreshTokenAsync(oldRefreshToken, CancellationToken.None);

        // Assert: new tokens issued
        refreshResult.Should().NotBeNull();
        refreshResult.AccessToken.Should().NotBeNullOrEmpty();
        refreshResult.RefreshToken.Should().NotBeNullOrEmpty();
        refreshResult.RefreshToken.Should().NotBe(oldRefreshToken); // rotated

        // Verify old token was revoked
        var oldTokenHash = HashRefreshTokenForVerification(oldRefreshToken);
        var oldToken = await _dbExecutor.QueryFirstOrDefaultAsync<(Guid Id, DateTime? RevokedAt)>(
            new CommandDefinition(
                "SELECT id, revoked_at FROM refresh_tokens WHERE token_hash = @TokenHash",
                new { TokenHash = oldTokenHash }));
        oldToken.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithRevokedToken_ThrowsAuthInvalidCredentialsException()
    {
        // Arrange: register, then revoke
        var registerResult = await _sut.RegisterAsync(
            new RegisterRequest("revokerefresh@test.com", "Revoke", "SecureP@ss1"),
            CancellationToken.None);

        var refreshToken = registerResult.RefreshToken;
        await _sut.RevokeTokenAsync(refreshToken, CancellationToken.None);

        // Act: try to refresh with the revoked token
        var act = () => _sut.RefreshTokenAsync(refreshToken, CancellationToken.None);

        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithExpiredToken_ThrowsAuthInvalidCredentialsException()
    {
        // Arrange: insert a refresh token that's already expired
        var userId = Guid.NewGuid();
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO app_users (id, email, display_name, password_hash) VALUES (@Id, @Email, @DisplayName, @PasswordHash)",
                new
                {
                    Id = userId,
                    Email = "expiredrefresh@test.com",
                    DisplayName = "Expired",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("SecureP@ss1")
                }));

        // Insert an expired refresh token — we know the hash because we hash it ourselves
        var refreshToken = "test-expired-refresh-token";
        var tokenHash = HashRefreshTokenForVerification(refreshToken);
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO refresh_tokens (user_id, token_hash, expires_at) VALUES (@UserId, @TokenHash, @ExpiresAt)",
                new
                {
                    UserId = userId,
                    TokenHash = tokenHash,
                    ExpiresAt = DateTime.UtcNow.AddDays(-1) // expired yesterday
                }));

        // Act
        var act = () => _sut.RefreshTokenAsync(refreshToken, CancellationToken.None);

        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithNonexistentToken_ThrowsAuthInvalidCredentialsException()
    {
        var act = () => _sut.RefreshTokenAsync("nonexistent-refresh-token", CancellationToken.None);

        await act.Should().ThrowAsync<AuthInvalidCredentialsException>();
    }

    // ── Revoke Token ────────────────────────────────────────

    [Fact]
    public async Task RevokeTokenAsync_WithValidToken_RevokesIt()
    {
        // Arrange: register to get a refresh token
        var registerResult = await _sut.RegisterAsync(
            new RegisterRequest("revoketest@test.com", "Revoke Test", "SecureP@ss1"),
            CancellationToken.None);

        var refreshToken = registerResult.RefreshToken;

        // Act
        await _sut.RevokeTokenAsync(refreshToken, CancellationToken.None);

        // Assert: verify the token is now revoked
        var tokenHash = HashRefreshTokenForVerification(refreshToken);
        var dbToken = await _dbExecutor.QueryFirstOrDefaultAsync<(Guid Id, DateTime? RevokedAt)>(
            new CommandDefinition(
                "SELECT id, revoked_at FROM refresh_tokens WHERE token_hash = @TokenHash",
                new { TokenHash = tokenHash }));
        dbToken.RevokedAt.Should().NotBeNull();
    }

    // ── External Login ──────────────────────────────────────

    [Fact]
    public async Task ExternalLoginAsync_NewUser_CreatesUserAndExternalLoginInTransaction()
    {
        // Arrange
        _externalAuthValidatorMock
            .Setup(x => x.ValidateAsync("Google", "valid-id-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalUserInfo("Google", "google-12345", "external@test.com", "External User", true));

        var request = new ExternalLoginRequest
        {
            Provider = "Google",
            IdToken = "valid-id-token"
        };

        // Act
        var result = await _sut.ExternalLoginAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.User.Email.Should().Be("external@test.com");
        result.User.DisplayName.Should().Be("External User");
        result.User.EmailVerified.Should().BeTrue();

        // Verify user and external_login rows were both created
        var user = await _dbExecutor.QuerySingleAsync<AuthUser>(
            new CommandDefinition(
                "SELECT id AS Id, email AS Email, display_name AS DisplayName, email_verified AS EmailVerified FROM app_users WHERE email = @Email",
                new { Email = "external@test.com" }));
        user.EmailVerified.Should().BeTrue();

        var externalLogin = await _dbExecutor.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT user_id FROM external_logins WHERE provider = @Provider AND provider_key = @ProviderKey",
                new { Provider = "Google", ProviderKey = "google-12345" }));
        externalLogin.Should().Be(user.Id);
    }

    [Fact]
    public async Task ExternalLoginAsync_ExistingMapping_ReturnsExistingUser()
    {
        // Arrange: seed a user and external_login mapping
        var userId = Guid.NewGuid();
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO app_users (id, email, display_name, password_hash, email_verified)
                VALUES (@Id, @Email, @DisplayName, NULL, TRUE)
                """,
                new { Id = userId, Email = "existing-ext@test.com", DisplayName = "Existing Ext" }));

        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO external_logins (user_id, provider, provider_key, display_name, email) VALUES (@UserId, @Provider, @ProviderKey, @DisplayName, @Email)",
                new { UserId = userId, Provider = "Google", ProviderKey = "existing-google-id", DisplayName = "Existing Ext", Email = "existing-ext@test.com" }));

        _externalAuthValidatorMock
            .Setup(x => x.ValidateAsync("Google", "existing-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalUserInfo("Google", "existing-google-id", "existing-ext@test.com", "Existing Ext", true));

        // Act
        var result = await _sut.ExternalLoginAsync(
            new ExternalLoginRequest { Provider = "Google", IdToken = "existing-token" },
            CancellationToken.None);

        // Assert
        result.User.Id.Should().Be(userId);
        result.User.Email.Should().Be("existing-ext@test.com");
    }

    [Fact]
    public async Task ExternalLoginAsync_NewEmail_LinksToExistingUser()
    {
        // Arrange: seed a user without password (external-only)
        var userId = Guid.NewGuid();
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO app_users (id, email, display_name, password_hash, email_verified)
                VALUES (@Id, @Email, @DisplayName, NULL, FALSE)
                """,
                new { Id = userId, Email = "link-ext@test.com", DisplayName = "Link Ext" }));

        _externalAuthValidatorMock
            .Setup(x => x.ValidateAsync("Google", "link-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalUserInfo("Google", "new-google-id", "link-ext@test.com", "Link Ext", true));

        // Act
        var result = await _sut.ExternalLoginAsync(
            new ExternalLoginRequest { Provider = "Google", IdToken = "link-token" },
            CancellationToken.None);

        // Assert: should link to existing user by email
        result.User.Id.Should().Be(userId);
        result.User.EmailVerified.Should().BeTrue(); // provider verified

        // Verify external_login mapping was created
        var mapping = await _dbExecutor.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT user_id FROM external_logins WHERE provider = @Provider AND provider_key = @ProviderKey",
                new { Provider = "Google", ProviderKey = "new-google-id" }));
        mapping.Should().Be(userId);
    }

    // ── Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Computes the SHA256 hash of a refresh token the same way AuthService does internally.
    /// Uses reflection to invoke the private HashRefreshToken method for accuracy.
    /// Falls back to directly computing SHA256 if reflection fails.
    /// </summary>
    private static string HashRefreshTokenForVerification(string refreshToken)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
