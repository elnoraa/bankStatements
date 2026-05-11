namespace Statements.WebAPI.Auth;

public sealed record JwtAccessToken(string Token, DateTimeOffset ExpiresAt);
