namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Public-facing auth response returned to the client.
/// The refresh token is set as an httpOnly cookie instead of being included in the body.
/// </summary>
public sealed record CookieAuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    AuthUserResponse User);
