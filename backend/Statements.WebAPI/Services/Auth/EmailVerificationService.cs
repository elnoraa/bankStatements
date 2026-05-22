using System.Security.Cryptography;
using System.Text;
using Dapper;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Email;

namespace Statements.WebAPI.Services.Auth;

/// <summary>
/// Handles email verification and password reset using token-based flows with SHA-256 hashed tokens.
/// </summary>
public sealed class EmailVerificationService : IEmailVerificationService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly IEmailService _emailService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(
        IDbExecutor dbExecutor,
        IEmailService emailService,
        IPasswordHasher passwordHasher,
        ILogger<EmailVerificationService> logger)
    {
        _dbExecutor = dbExecutor;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendVerificationEmailAsync(Guid userId, string email, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending verification email to user {UserId} at {Email}", userId, email);

        var token = GenerateToken();
        var tokenHash = HashToken(token);
        var expiresAt = DateTime.UtcNow.AddHours(1);

        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO email_tokens (user_id, token_hash, token_type, expires_at)
                VALUES (@UserId, @TokenHash, 'email_verification', @ExpiresAt)
                """,
                new { UserId = userId, TokenHash = tokenHash, ExpiresAt = expiresAt },
                cancellationToken: cancellationToken));

        var body = $"Your email verification code is:\n\n{token}\n\nThis code expires in 1 hour.";
        await _emailService.SendAsync(email, "Verify your email address", body, cancellationToken);

        _logger.LogInformation("Verification email sent to user {UserId}", userId);
    }

    /// <inheritdoc />
    public async Task VerifyEmailAsync(string token, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Email verification attempt");

        var tokenHash = HashToken(token);

        var storedToken = await _dbExecutor.QueryFirstOrDefaultAsync<(Guid Id, Guid UserId, DateTime ExpiresAt, DateTime? UsedAt)>(
            new CommandDefinition(
                "SELECT id, user_id, expires_at, used_at FROM email_tokens WHERE token_hash = @TokenHash AND token_type = 'email_verification'",
                new { TokenHash = tokenHash },
                cancellationToken: cancellationToken));

        if (storedToken.Id == Guid.Empty)
        {
            _logger.LogWarning("Email verification failed - token not found");
            throw new InvalidOperationException("Invalid verification token.");
        }

        if (storedToken.UsedAt.HasValue)
        {
            _logger.LogWarning("Email verification failed - token already used");
            throw new InvalidOperationException("Verification token has already been used.");
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Email verification failed - token expired");
            throw new InvalidOperationException("Verification token has expired.");
        }

        // Mark token as used and update user
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE email_tokens SET used_at = NOW() WHERE id = @Id;
                UPDATE app_users SET email_verified = TRUE, updated_at = NOW() WHERE id = @UserId
                """,
                new { Id = storedToken.Id, UserId = storedToken.UserId },
                cancellationToken: cancellationToken));

        _logger.LogInformation("Email verified successfully for user {UserId}", storedToken.UserId);
    }

    /// <inheritdoc />
    public async Task SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Password reset requested for email: {Email}", email);

        var user = await _dbExecutor.QueryFirstOrDefaultAsync<AuthUser>(
            new CommandDefinition(
                "SELECT id AS Id, email AS Email, display_name AS DisplayName FROM app_users WHERE LOWER(email) = LOWER(@Email)",
                new { Email = email },
                cancellationToken: cancellationToken));

        // Always return success to prevent email enumeration
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogInformation("Password reset requested for non-existent email: {Email} (silently ignored)", email);
            return;
        }

        var token = GenerateToken();
        var tokenHash = HashToken(token);
        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO email_tokens (user_id, token_hash, token_type, expires_at)
                VALUES (@UserId, @TokenHash, 'password_reset', @ExpiresAt)
                """,
                new { UserId = user.Id, TokenHash = tokenHash, ExpiresAt = expiresAt },
                cancellationToken: cancellationToken));

        var body = $"Your password reset code is:\n\n{token}\n\nThis code expires in 15 minutes.";
        await _emailService.SendAsync(email, "Reset your password", body, cancellationToken);

        _logger.LogInformation("Password reset email sent to user {UserId}", user.Id);
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Password reset attempt");

        var tokenHash = HashToken(token);

        var storedToken = await _dbExecutor.QueryFirstOrDefaultAsync<(Guid Id, Guid UserId, DateTime ExpiresAt, DateTime? UsedAt)>(
            new CommandDefinition(
                "SELECT id, user_id, expires_at, used_at FROM email_tokens WHERE token_hash = @TokenHash AND token_type = 'password_reset'",
                new { TokenHash = tokenHash },
                cancellationToken: cancellationToken));

        if (storedToken.Id == Guid.Empty)
        {
            _logger.LogWarning("Password reset failed - token not found");
            throw new InvalidOperationException("Invalid reset token.");
        }

        if (storedToken.UsedAt.HasValue)
        {
            _logger.LogWarning("Password reset failed - token already used");
            throw new InvalidOperationException("Reset token has already been used.");
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Password reset failed - token expired");
            throw new InvalidOperationException("Reset token has expired.");
        }

        var passwordHash = _passwordHasher.Hash(newPassword);

        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE email_tokens SET used_at = NOW() WHERE id = @Id;
                UPDATE app_users SET password_hash = @PasswordHash, email_verified = TRUE, updated_at = NOW() WHERE id = @UserId
                """,
                new { Id = storedToken.Id, UserId = storedToken.UserId, PasswordHash = passwordHash },
                cancellationToken: cancellationToken));

        _logger.LogInformation("Password reset successfully for user {UserId}", storedToken.UserId);
    }

    /// <summary>
    /// Generates a cryptographically random token string (URL-safe base64, no padding).
    /// </summary>
    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    /// <summary>
    /// Computes the SHA-256 hash of a token for secure database storage.
    /// </summary>
    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
