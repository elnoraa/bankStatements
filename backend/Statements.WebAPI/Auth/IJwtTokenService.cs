using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Auth;

/// <summary>
/// Provides JWT access token creation for authenticated users.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Creates a signed JWT access token for the specified user.
    /// </summary>
    /// <param name="user">The authenticated user to create the token for.</param>
    /// <returns>A <see cref="JwtAccessToken"/> containing the token string and expiration.</returns>
    JwtAccessToken CreateAccessToken(AuthUser user);
}
