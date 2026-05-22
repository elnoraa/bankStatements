using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Services.Auth
{
    /// <summary>
    /// Provides authentication operations including registration, login, external auth, token management,
    /// email verification, and password reset.
    /// </summary>
    public interface IAuthService
    {
        /// <summary>
        /// Registers a new user account.
        /// </summary>
        /// <param name="request">The registration details including email, display name, and password.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An <see cref="AuthResponse"/> containing tokens and user profile.</returns>
        /// <exception cref="AuthConflictException">Thrown when the email is already registered.</exception>
        Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Authenticates a user with email and password credentials.
        /// </summary>
        /// <param name="request">The login credentials.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An <see cref="AuthResponse"/> containing tokens and user profile.</returns>
        /// <exception cref="AuthInvalidCredentialsException">Thrown when credentials are invalid or email not verified.</exception>
        /// <exception cref="AuthAccountLockedException">Thrown when the account is temporarily locked.</exception>
        Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Authenticates or links a user via an external OAuth/OpenID identity token.
        /// </summary>
        /// <param name="request">The external login details including provider and ID token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An <see cref="AuthResponse"/> containing tokens and user profile.</returns>
        Task<AuthResponse> ExternalLoginAsync(ExternalLoginRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Refreshes the access and refresh tokens using a valid refresh token.
        /// </summary>
        /// <param name="refreshToken">The refresh token to validate and exchange.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An <see cref="AuthResponse"/> containing new tokens and user profile.</returns>
        /// <exception cref="AuthInvalidCredentialsException">Thrown when the refresh token is invalid, expired, or revoked.</exception>
        Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

        /// <summary>
        /// Revokes a refresh token, rendering it unusable for future token refresh requests.
        /// </summary>
        /// <param name="refreshToken">The refresh token to revoke.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken);

        /// <summary>
        /// Verifies a user's email address using a verification token.
        /// </summary>
        /// <param name="token">The verification token sent to the user's email.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when the token is invalid, expired, or already used.</exception>
        Task VerifyEmailAsync(string token, CancellationToken cancellationToken);

        /// <summary>
        /// Initiates a password reset for the specified email address.
        /// If the email exists, a reset token is sent. No error is returned if the email doesn't exist (anti-enumeration).
        /// </summary>
        /// <param name="email">The email address to send the reset token to.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ForgotPasswordAsync(string email, CancellationToken cancellationToken);

        /// <summary>
        /// Resets a user's password using a valid reset token.
        /// </summary>
        /// <param name="token">The reset token sent to the user's email.</param>
        /// <param name="newPassword">The new password to set.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when the token is invalid, expired, or already used.</exception>
        Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken);
    }
}
