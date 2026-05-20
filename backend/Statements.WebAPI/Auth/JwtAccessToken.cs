namespace Statements.WebAPI.Auth;

/// <summary>
/// Represents a signed JWT access token with its expiration time.
/// </summary>
/// <param name="Token">The serialized JWT token string.</param>
/// <param name="ExpiresAt">The date and time when the token expires.</param>
public sealed record JwtAccessToken(string Token, DateTimeOffset ExpiresAt);
