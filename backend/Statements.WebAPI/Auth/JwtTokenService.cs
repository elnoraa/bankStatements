using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Auth;

/// <summary>
/// Service for creating signed JWT access tokens using HMAC-SHA256.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly ILogger<JwtTokenService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenService"/> class.
    /// </summary>
    /// <param name="options">JWT configuration options (secret, issuer, audience, expiry).</param>
    /// <param name="logger">Logger instance.</param>
    public JwtTokenService(IOptions<JwtOptions> options, ILogger<JwtTokenService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public JwtAccessToken CreateAccessToken(AuthUser user)
    {
        if (string.IsNullOrWhiteSpace(_options.Secret))
        {
            _logger.LogError("JWT secret is not configured.");
            throw new InvalidOperationException("JWT secret is not configured.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        _logger.LogDebug("Access token created for user {UserId} (expires: {ExpiresAt})", user.Id, expiresAt);

        return new JwtAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
