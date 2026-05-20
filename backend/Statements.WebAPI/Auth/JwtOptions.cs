namespace Statements.WebAPI.Auth;

/// <summary>
/// Configuration options for JWT token generation.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// The token issuer identifier.
    /// </summary>
    public string Issuer { get; init; } = "Statements.WebAPI";

    /// <summary>
    /// The intended audience for the token.
    /// </summary>
    public string Audience { get; init; } = "Statements.Client";

    /// <summary>
    /// The secret key used to sign the JWT. Must be configured in application settings.
    /// </summary>
    public string Secret { get; init; } = string.Empty;

    /// <summary>
    /// Access token lifetime in minutes. Default is 15 minutes.
    /// </summary>
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>
    /// Refresh token lifetime in days. Default is 30 days.
    /// </summary>
    public int RefreshTokenDays { get; init; } = 30;
}
