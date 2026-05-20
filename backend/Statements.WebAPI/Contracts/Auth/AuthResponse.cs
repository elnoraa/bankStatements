namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Internal auth response containing both access and refresh tokens along with user information.
/// The refresh token is also set as an httpOnly cookie by the controller.
/// </summary>
/// <param name="AccessToken">The JWT access token for API authorization.</param>
/// <param name="AccessTokenExpiresAt">The date and time when the access token expires.</param>
/// <param name="RefreshToken">The refresh token used to obtain a new access token.</param>
/// <param name="RefreshTokenExpiresAt">The date and time when the refresh token expires.</param>
/// <param name="User">The authenticated user's profile information.</param>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthUserResponse User);
