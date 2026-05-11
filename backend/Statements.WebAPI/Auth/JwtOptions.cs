namespace Statements.WebAPI.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "Statements.WebAPI";
    public string Audience { get; init; } = "Statements.Client";
    public string Secret { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}
