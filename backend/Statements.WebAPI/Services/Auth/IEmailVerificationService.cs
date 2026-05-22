namespace Statements.WebAPI.Services.Auth;

/// <summary>
/// Provides email verification and password reset operations using token-based flows.
/// </summary>
public interface IEmailVerificationService
{
    /// <summary>
    /// Generates an email verification token, persists it, and sends the verification email.
    /// </summary>
    /// <param name="userId">The user ID to verify.</param>
    /// <param name="email">The email address to send the verification to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendVerificationEmailAsync(Guid userId, string email, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies an email address using a verification token.
    /// </summary>
    /// <param name="token">The verification token from the email.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when the token is invalid, expired, or already used.</exception>
    Task VerifyEmailAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Generates a password reset token, persists it, and sends the reset email.
    /// </summary>
    /// <param name="email">The email address to send the reset link to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Resets a user's password using a valid reset token.
    /// </summary>
    /// <param name="token">The reset token from the email.</param>
    /// <param name="newPassword">The new password to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when the token is invalid, expired, or already used.</exception>
    Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken);
}
