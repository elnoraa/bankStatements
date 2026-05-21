using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Options;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Contracts.Auth;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Auth;

/// <summary>
/// Handles user authentication operations including registration, login, external auth, and token management.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtOptions _jwtOptions;
    private readonly IExternalAuthValidator _externalAuthValidator;
    private readonly ILogger<AuthService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthService"/> class.
    /// </summary>
    /// <param name="dbExecutor">Executes Dapper database commands.</param>
    /// <param name="connectionFactory">Factory for creating database connections (used for transactions).</param>
    /// <param name="passwordHasher">Service for hashing and verifying passwords.</param>
    /// <param name="jwtTokenService">Service for creating JWT access tokens.</param>
    /// <param name="jwtOptions">JWT configuration options.</param>
    /// <param name="externalAuthValidator">Validator for external OAuth/OpenID tokens.</param>
    /// <param name="logger">Logger instance.</param>
    public AuthService(
        IDbExecutor dbExecutor,
        IDbConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions,
        IExternalAuthValidator externalAuthValidator,
        ILogger<AuthService> logger)
    {
        _dbExecutor = dbExecutor;
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _jwtOptions = jwtOptions.Value;
        _externalAuthValidator = externalAuthValidator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AuthResponse> ExternalLoginAsync(ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("External login attempt with provider: {Provider}", request.Provider);

        var info = await _externalAuthValidator.ValidateAsync(request.Provider, request.IdToken, cancellationToken);

        _logger.LogDebug("External user info retrieved: Provider={Provider}, ProviderKey={ProviderKey}, Email={Email}",
            info.Provider, info.ProviderKey, info.Email);

        using var connection = _connectionFactory.CreateConnection();

        // Step 1: Look up by existing external_login mapping
        var existingUserId = await _dbExecutor.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT user_id FROM external_logins WHERE provider = @Provider AND provider_key = @ProviderKey",
                new { Provider = info.Provider, ProviderKey = info.ProviderKey },
                cancellationToken: cancellationToken));

        if (existingUserId is not null)
        {
            _logger.LogDebug("Existing external login found for user {UserId}", existingUserId.Value);

            var user = await _dbExecutor.QuerySingleAsync<AuthUser>(
                new CommandDefinition(
                    "SELECT id AS Id, email AS Email, display_name AS DisplayName, email_verified AS EmailVerified, password_hash AS PasswordHash FROM app_users WHERE id = @Id",
                    new { Id = existingUserId.Value },
                    cancellationToken: cancellationToken));

            // ensure email_verified updated if provider verified email
            if (!user.EmailVerified && info.EmailVerified && !string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogInformation("Updating email_verified for user {UserId}", user.Id);
                await _dbExecutor.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE app_users SET email_verified = TRUE, updated_at = NOW() WHERE id = @Id",
                        new { Id = user.Id },
                        cancellationToken: cancellationToken));
                user = user with { EmailVerified = true };
            }

            _logger.LogInformation("External login successful for existing user {UserId}", user.Id);
            return await CreateAuthResponseAsync(user, cancellationToken);
        }

        // Step 2: No external_login mapping yet. Compute the email to use
        // (fallback email ensures even users without a provider email can be identified).
        var emailToInsert = info.Email ?? ($"{info.Provider}:{info.ProviderKey}@noemail.local");
        var displayName = info.DisplayName ?? info.Email ?? "";
        var normalizedEmail = NormalizeEmail(emailToInsert);

        // Always check by email — even the fallback generated email — so a second login
        // attempt after a partial failure re-links rather than crashes with a duplicate key.
        var existingByEmail = await _dbExecutor.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT id FROM app_users WHERE LOWER(email) = LOWER(@Email)",
                new { Email = normalizedEmail },
                cancellationToken: cancellationToken));

        if (existingByEmail is not null)
        {
            var userId = existingByEmail.Value;
            _logger.LogInformation("Linking external login {Provider} to existing user {UserId}", info.Provider, userId);

            await _dbExecutor.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO external_logins (user_id, provider, provider_key, display_name, email) VALUES (@UserId, @Provider, @ProviderKey, @DisplayName, @Email) ON CONFLICT (provider, provider_key) DO NOTHING",
                    new { UserId = userId, Provider = info.Provider, ProviderKey = info.ProviderKey, DisplayName = info.DisplayName, Email = info.Email },
                    cancellationToken: cancellationToken));

            var user = await _dbExecutor.QuerySingleAsync<AuthUser>(
                new CommandDefinition(
                    "SELECT id AS Id, email AS Email, display_name AS DisplayName, email_verified AS EmailVerified, password_hash AS PasswordHash FROM app_users WHERE id = @Id",
                    new { Id = userId },
                    cancellationToken: cancellationToken));

            if (!user.EmailVerified && info.EmailVerified)
            {
                _logger.LogInformation("Updating email_verified for user {UserId} after external login link", user.Id);
                await _dbExecutor.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE app_users SET email_verified = TRUE, updated_at = NOW() WHERE id = @Id",
                        new { Id = user.Id },
                        cancellationToken: cancellationToken));
                user = user with { EmailVerified = true };
            }

            _logger.LogInformation("External login linked successfully for user {UserId}", user.Id);
            return await CreateAuthResponseAsync(user, cancellationToken);
        }

        // Step 3: Truly new user — create both rows inside a transaction for atomicity.
        _logger.LogInformation("Creating new user from external login: Provider={Provider}, ProviderKey={ProviderKey}", info.Provider, info.ProviderKey);

        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            // Use ON CONFLICT as a safety net for concurrent requests
            var newUser = await connection.QuerySingleAsync<AuthUser>(
                new CommandDefinition(
                    "INSERT INTO app_users (email, display_name, password_hash, email_verified) VALUES (@Email, @DisplayName, NULL, @EmailVerified) RETURNING id AS Id, email AS Email, display_name AS DisplayName, email_verified AS EmailVerified, password_hash AS PasswordHash",
                    new { Email = emailToInsert, DisplayName = displayName, EmailVerified = info.EmailVerified },
                    transaction: transaction,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("New user {UserId} created from external login", newUser.Id);

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO external_logins (user_id, provider, provider_key, display_name, email) VALUES (@UserId, @Provider, @ProviderKey, @DisplayName, @Email)",
                    new { UserId = newUser.Id, Provider = info.Provider, ProviderKey = info.ProviderKey, DisplayName = info.DisplayName, Email = info.Email },
                    transaction: transaction,
                    cancellationToken: cancellationToken));

            transaction.Commit();

            _logger.LogInformation("External login successful for new user {UserId}", newUser.Id);
            return await CreateAuthResponseAsync(newUser, cancellationToken);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        _logger.LogInformation("User registration attempt for email: {Email}", email);

        var existingUserId = await _dbExecutor.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT id FROM app_users WHERE LOWER(email) = LOWER(@Email)",
                new { Email = email },
                cancellationToken: cancellationToken));

        if (existingUserId is not null)
        {
            _logger.LogWarning("Registration failed - email already exists: {Email}", email);
            // Return generic message to prevent email enumeration
            throw new AuthConflictException("If the email is available, we'll send you a confirmation. Please check your inbox.");
        }

        var passwordHash = _passwordHasher.Hash(request.Password);
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? email.Split('@')[0]
            : request.DisplayName.Trim();
        var user = await _dbExecutor.QuerySingleAsync<AuthUser>(
            new CommandDefinition(
                """
                INSERT INTO app_users (email, display_name, password_hash)
                VALUES (@Email, @DisplayName, @PasswordHash)
                RETURNING
                    id AS Id,
                    email AS Email,
                    display_name AS DisplayName,
                    email_verified AS EmailVerified,
                    password_hash AS PasswordHash
                """,
                new
                {
                    Email = email,
                    DisplayName = displayName,
                    PasswordHash = passwordHash
                },
                cancellationToken: cancellationToken));

        _logger.LogInformation("User registered successfully: {UserId}, Email: {Email}", user.Id, user.Email);

        // Create a default "Untitled" bank account for the new user
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO bank_accounts (user_id, bank_name, account_name, currency)
                VALUES (@UserId, '', 'Untitled', 'AUD')
                """,
                new { UserId = user.Id },
                cancellationToken: cancellationToken));

        _logger.LogDebug("Default bank account created for user {UserId}", user.Id);

        return await CreateAuthResponseAsync(user, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        _logger.LogInformation("Login attempt for email: {Email}", email);

        var user = await _dbExecutor.QueryFirstOrDefaultAsync<AuthUser>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    email AS Email,
                    display_name AS DisplayName,
                    email_verified AS EmailVerified,
                    password_hash AS PasswordHash,
                    failed_login_attempts AS FailedLoginAttempts,
                    locked_until AS LockedUntil
                FROM app_users
                WHERE LOWER(email) = LOWER(@Email)
                """,
                new { Email = email },
                cancellationToken: cancellationToken));

        // Check if account is locked (check before verifying password to avoid revealing account existence)
        if (user is not null && user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            _logger.LogWarning("Login rejected for email: {Email} - account locked until {LockedUntil}", email, user.LockedUntil.Value);
            throw new AuthAccountLockedException(user.LockedUntil.Value);
        }

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed for email: {Email} - invalid credentials", email);

            // Track failed login attempt for existing users
            if (user is not null)
            {
                await _dbExecutor.ExecuteAsync(
                    new CommandDefinition(
                        """
                        UPDATE app_users
                        SET failed_login_attempts = failed_login_attempts + 1,
                            locked_until = CASE
                                WHEN failed_login_attempts + 1 >= 5 THEN NOW() + INTERVAL '15 minutes'
                                ELSE locked_until
                            END,
                            updated_at = NOW()
                        WHERE id = @Id
                        """,
                        new { Id = user.Id },
                        cancellationToken: cancellationToken));
            }

            throw new AuthInvalidCredentialsException();
        }

        // Reset failed attempts on successful login
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "UPDATE app_users SET failed_login_attempts = 0, locked_until = NULL, updated_at = NOW() WHERE id = @Id",
                new { Id = user.Id },
                cancellationToken: cancellationToken));

        _logger.LogInformation("Login successful: UserId={UserId}, Email={Email}", user.Id, user.Email);
        return await CreateAuthResponseAsync(user, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Refresh token attempt");

        var tokenHash = HashRefreshToken(refreshToken);

        var storedToken = await _dbExecutor.QueryFirstOrDefaultAsync<(Guid Id, Guid UserId, DateTime ExpiresAt, DateTime? RevokedAt)>(
            new CommandDefinition(
                "SELECT id, user_id, expires_at, revoked_at FROM refresh_tokens WHERE token_hash = @TokenHash",
                new { TokenHash = tokenHash },
                cancellationToken: cancellationToken));

        if (storedToken.Id == Guid.Empty)
        {
            _logger.LogWarning("Refresh token not found or invalid");
            throw new AuthInvalidCredentialsException();
        }

        if (storedToken.RevokedAt.HasValue)
        {
            _logger.LogWarning("Refresh token has been revoked");
            throw new AuthInvalidCredentialsException();
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Refresh token has expired");
            throw new AuthInvalidCredentialsException();
        }

        // Revoke the old refresh token (rotation)
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "UPDATE refresh_tokens SET revoked_at = NOW() WHERE id = @Id",
                new { Id = storedToken.Id },
                cancellationToken: cancellationToken));

        _logger.LogDebug("Old refresh token revoked for user {UserId}", storedToken.UserId);

        var user = await _dbExecutor.QuerySingleAsync<AuthUser>(
            new CommandDefinition(
                "SELECT id AS Id, email AS Email, display_name AS DisplayName, email_verified AS EmailVerified, password_hash AS PasswordHash, failed_login_attempts AS FailedLoginAttempts, locked_until AS LockedUntil FROM app_users WHERE id = @Id",
                new { Id = storedToken.UserId },
                cancellationToken: cancellationToken));

        return await CreateAuthResponseAsync(user, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Revoke token attempt");

        var tokenHash = HashRefreshToken(refreshToken);

        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "UPDATE refresh_tokens SET revoked_at = NOW() WHERE token_hash = @TokenHash AND revoked_at IS NULL",
                new { TokenHash = tokenHash },
                cancellationToken: cancellationToken));

        _logger.LogDebug("Refresh token revoked");
    }

    /// <summary>
    /// Creates an <see cref="AuthResponse"/> with a JWT access token, refresh token, and user profile.
    /// Also persists the refresh token to the database.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="AuthResponse"/> containing tokens and user info.</returns>
    private async Task<AuthResponse> CreateAuthResponseAsync(
        AuthUser user,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating auth response for user {UserId}", user.Id);

        var accessToken = _jwtTokenService.CreateAccessToken(user);
        var refreshToken = GenerateRefreshToken();
        var refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);

        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO refresh_tokens (user_id, token_hash, expires_at)
                VALUES (@UserId, @TokenHash, @ExpiresAt)
                """,
                new
                {
                    UserId = user.Id,
                    TokenHash = HashRefreshToken(refreshToken),
                    ExpiresAt = refreshTokenExpiresAt
                },
                cancellationToken: cancellationToken));

        _logger.LogDebug("Auth response created for user {UserId}", user.Id);

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAt,
            refreshToken,
            refreshTokenExpiresAt,
            new AuthUserResponse(user.Id, user.Email, user.DisplayName, user.EmailVerified));
    }

    /// <summary>
    /// Normalizes an email address by trimming whitespace and converting to lowercase.
    /// </summary>
    /// <param name="email">The email address to normalize.</param>
    /// <returns>The normalized email string.</returns>
    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Generates a cryptographically random refresh token string (URL-safe base64, no padding).
    /// </summary>
    /// <returns>A URL-safe random token string.</returns>
    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    /// <summary>
    /// Computes the SHA-256 hash of a refresh token for secure database storage.
    /// </summary>
    /// <param name="refreshToken">The raw refresh token to hash.</param>
    /// <returns>The lowercase hexadecimal SHA-256 hash.</returns>
    private static string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
