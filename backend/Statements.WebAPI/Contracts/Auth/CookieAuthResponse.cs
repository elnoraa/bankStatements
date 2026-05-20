namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Public-facing auth response returned to the client.
/// The refresh token is set as an httpOnly cookie instead of being included in the body.
/// </summary>
/// <param name="AccessToken">The JWT access token for API authorization.</param>
/// <param name="AccessTokenExpiresAt">The date and time when the access token expires.</param>
/// <param name="User">The authenticated user's profile information.</param>
public sealed record CookieAuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    AuthUserResponse User);
