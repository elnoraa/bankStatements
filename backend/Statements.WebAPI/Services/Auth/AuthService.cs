using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Options;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Contracts.Auth;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtOptions _jwtOptions;
    private readonly IExternalAuthValidator _externalAuthValidator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IDbConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions,
        IExternalAuthValidator externalAuthValidator,
        ILogger<AuthService> logger)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _jwtOptions = jwtOptions.Value;
        _externalAuthValidator = externalAuthValidator;
        _logger = logger;
    }

    public async Task<AuthResponse> ExternalLoginAsync(ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("External login attempt with provider: {Provider}", request.Provider);

        var info = await _externalAuthValidator.ValidateAsync(request.Provider, request.IdToken, cancellationToken);

        _logger.LogDebug("External user info retrieved: Provider={Provider}, ProviderKey={ProviderKey}, Email={Email}",
            info.Provider, info.ProviderKey, info.Email);

        using var connection = _connectionFactory.CreateConnection();

        var existingUserId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT user_id FROM external_logins WHERE provider = @Provider AND provider_key = @ProviderKey",
                new { Provider = info.Provider, ProviderKey = info.ProviderKey },
                cancellationToken: cancellationToken));

        if (existingUserId is not null)
        {
            _logger.LogDebug("Existing external login found for user {UserId}", existingUserId.Value);

            var user = await connection.QuerySingleAsync<AuthUser>(
                new CommandDefinition(
                    "SELECT id AS Id, email AS Email, display_name AS DisplayName, email_verified AS EmailVerified, password_hash AS PasswordHash FROM app_users WHERE id = @Id",
                    new { Id = existingUserId.Value },
                    cancellationToken: cancellationToken));

            // ensure email_verified updated if provider verified email
            if (!user.EmailVerified && info.EmailVerified && !string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogInformation("Updating email_verified for user {UserId}", user.Id);
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE app_users SET email_verified = TRUE, updated_at = NOW() WHERE id = @Id",
                        new { Id = user.Id },
                        cancellationToken: cancellationToken));
                user = user with { EmailVerified = true };
            }

            _logger.LogInformation("External login successful for existing user {UserId}", user.Id);
            return await CreateAuthResponseAsync(connection, user, cancellationToken);
        }

        // No external login mapped yet. Try to find by email (if available).
        Guid userId;
        var normalizedEmail = !string.IsNullOrWhiteSpace(info.Email) ? NormalizeEmail(info.Email) : null;
        if (normalizedEmail is not null)
        {
            var existingByEmail = await connection.QueryFirstOrDefaultAsync<Guid?>(
                new CommandDefinition(
                    "SELECT id FROM app_users WHERE LOWER(email) = LOWER(@Email)",
                    new { Email = normalizedEmail },
                    cancellationToken: cancellationToken));

            if (existingByEmail is not null)
            {
                userId = existingByEmail.Value;
                _logger.LogInformation("Linking external login {Provider} to existing user {UserId}", info.Provider, userId);

                // link external login (avoid duplicate error)
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "INSERT INTO external_logins (user_id, provider, provider_key, display_name, email) VALUES (@UserId, @Provider, @ProviderKey, @DisplayName, @Email) ON CONFLICT (provider, provider_key) DO NOTHING",
                        new { UserId = userId, Provider = info.Provider, ProviderKey = info.ProviderKey, DisplayName = info.DisplayName, Email = info.Email },
                        cancellationToken: cancellationToken));

                // if provider verified email, ensure app marks email_verified
                var user = await connection.QuerySingleAsync<AuthUser>(
                    new CommandDefinition(
                        "SELECT id AS Id, email AS Email, display_name AS DisplayName, email_verified AS EmailVerified, password_hash AS PasswordHash FROM app_users WHERE id = @Id",
                        new { Id = userId },
                        cancellationToken: cancellationToken));

                if (!user.EmailVerified && info.EmailVerified)
                {
                    _logger.LogInformation("Updating email_verified for user {UserId} after external login link", user.Id);
                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            "UPDATE app_users SET email_verified = TRUE, updated_at = NOW() WHERE id = @Id",
                            new { Id = user.Id },
                            cancellationToken: cancellationToken));
                    user = user with { EmailVerified = true };
                }

                _logger.LogInformation("External login linked successfully for user {UserId}", user.Id);
                return await CreateAuthResponseAsync(connection, user, cancellationToken);
            }
        }

        // Create a new user
        _logger.LogInformation("Creating new user from external login: Provider={Provider}, ProviderKey={ProviderKey}", info.Provider, info.ProviderKey);

        var displayName = info.DisplayName ?? info.Email ?? "";
        var emailToInsert = info.Email ?? ($"{info.Provider}:{info.ProviderKey}@noemail.local");
        var newUser = await connection.QuerySingleAsync<AuthUser>(
            new CommandDefinition(
                "INSERT INTO app_users (email, display_name, password_hash, email_verified) VALUES (@Email, @DisplayName, NULL, @EmailVerified) RETURNING id AS Id, email AS Email, display_name AS DisplayName, email_verified AS EmailVerified, password_hash AS PasswordHash",
                new { Email = emailToInsert, DisplayName = displayName, EmailVerified = info.EmailVerified },
                cancellationToken: cancellationToken));

        _logger.LogInformation("New user {UserId} created from external login", newUser.Id);

        await connection.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO external_logins (user_id, provider, provider_key, display_name, email) VALUES (@UserId, @Provider, @ProviderKey, @DisplayName, @Email)",
                new { UserId = newUser.Id, Provider = info.Provider, ProviderKey = info.ProviderKey, DisplayName = info.DisplayName, Email = info.Email },
                cancellationToken: cancellationToken));

        _logger.LogInformation("External login successful for new user {UserId}", newUser.Id);
        return await CreateAuthResponseAsync(connection, newUser, cancellationToken);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        _logger.LogInformation("User registration attempt for email: {Email}", email);

        using var connection = _connectionFactory.CreateConnection();
        var existingUserId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT id FROM app_users WHERE LOWER(email) = LOWER(@Email)",
                new { Email = email },
                cancellationToken: cancellationToken));

        if (existingUserId is not null)
        {
            _logger.LogWarning("Registration failed - email already exists: {Email}", email);
            throw new AuthConflictException("A user with this email already exists.");
        }

        var passwordHash = _passwordHasher.Hash(request.Password);
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? email.Split('@')[0]
            : request.DisplayName.Trim();
        var user = await connection.QuerySingleAsync<AuthUser>(
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
        return await CreateAuthResponseAsync(connection, user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        _logger.LogInformation("Login attempt for email: {Email}", email);

        using var connection = _connectionFactory.CreateConnection();
        var user = await connection.QueryFirstOrDefaultAsync<AuthUser>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    email AS Email,
                    display_name AS DisplayName,
                    email_verified AS EmailVerified,
                    password_hash AS PasswordHash
                FROM app_users
                WHERE LOWER(email) = LOWER(@Email)
                """,
                new { Email = email },
                cancellationToken: cancellationToken));

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed for email: {Email} - invalid credentials", email);
            throw new AuthInvalidCredentialsException();
        }

        _logger.LogInformation("Login successful: UserId={UserId}, Email={Email}", user.Id, user.Email);
        return await CreateAuthResponseAsync(connection, user, cancellationToken);
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(
        System.Data.IDbConnection connection,
        AuthUser user,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating auth response for user {UserId}", user.Id);

        var accessToken = _jwtTokenService.CreateAccessToken(user);
        var refreshToken = GenerateRefreshToken();
        var refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);

        await connection.ExecuteAsync(
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

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
