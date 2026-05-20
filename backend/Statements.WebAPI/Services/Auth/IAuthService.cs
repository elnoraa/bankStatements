using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Services.Auth
{
    /// <summary>
    /// Provides authentication operations including registration, login, external auth, and token management.
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
        /// <exception cref="AuthUnauthorizedException">Thrown when credentials are invalid.</exception>
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
        /// <exception cref="AuthUnauthorizedException">Thrown when the refresh token is invalid, expired, or revoked.</exception>
        Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

        /// <summary>
        /// Revokes a refresh token, rendering it unusable for future token refresh requests.
        /// </summary>
        /// <param name="refreshToken">The refresh token to revoke.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken);
    }
}
