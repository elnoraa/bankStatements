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

    public AuthService(
        IDbConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);

        using var connection = _connectionFactory.CreateConnection();
        var existingUserId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(
                "SELECT id FROM app_users WHERE LOWER(email) = LOWER(@Email)",
                new { Email = email },
                cancellationToken: cancellationToken));

        if (existingUserId is not null)
        {
            throw new AuthConflictException("A user with this email already exists.");
        }

        var passwordHash = _passwordHasher.Hash(request.Password);
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
                    DisplayName = request.DisplayName.Trim(),
                    PasswordHash = passwordHash
                },
                cancellationToken: cancellationToken));

        return await CreateAuthResponseAsync(connection, user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);

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
            throw new AuthInvalidCredentialsException();
        }

        return await CreateAuthResponseAsync(connection, user, cancellationToken);
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(
        System.Data.IDbConnection connection,
        AuthUser user,
        CancellationToken cancellationToken)
    {
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
