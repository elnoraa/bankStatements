using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Statements.WebAPI.Services.Auth;

internal sealed class ProviderConfig
{
    public string Authority { get; init; } = null!;
    public string? ClientId { get; init; }
    public string? Audience { get; init; }
}

public sealed class ExternalAuthValidator : IExternalAuthValidator
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalAuthValidator> _logger;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expiry, JsonWebKeySet Jwks, string Issuer)> _cache = new();

    public ExternalAuthValidator(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<ExternalAuthValidator> logger)
    {
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient("external-auth");
        _logger = logger;
    }

    public async Task<ExternalUserInfo> ValidateAsync(string provider, string idToken, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Validating external token for provider: {Provider}", provider);

        var section = _configuration.GetSection($"ExternalProviders:{provider}");
        if (!section.Exists())
        {
            _logger.LogError("Provider configuration not found: {Provider}", provider);
            throw new InvalidOperationException($"Provider configuration not found: {provider}");
        }

        var cfg = section.Get<ProviderConfig>() ?? throw new InvalidOperationException("Invalid provider config");
        var (jwks, issuer) = await GetJwksAsync(cfg.Authority, cancellationToken);

        _logger.LogDebug("JWKS retrieved for provider {Provider}, issuer: {Issuer}", provider, issuer);

        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudiences = cfg.ClientId is not null ? new[] { cfg.ClientId } : (cfg.Audience is not null ? new[] { cfg.Audience } : null),
            ValidateLifetime = true,
            IssuerSigningKeys = jwks.GetSigningKeys()
        };

        try
        {
            var principal = handler.ValidateToken(idToken, validationParameters, out var validatedToken);
            var jwt = (JwtSecurityToken)validatedToken;
            var providerKey = jwt.Subject ?? principal.FindFirst("sub")?.Value ?? throw new SecurityTokenException("No subject");
            // Read claims from the JWT token directly to avoid MapInboundClaims mapping issues
            var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value
                        ?? principal.FindFirst(ClaimTypes.Email)?.Value;
            var emailVerifiedStr = jwt.Claims.FirstOrDefault(c => c.Type == "email_verified")?.Value
                                   ?? principal.FindFirst("email_verified")?.Value;
            var emailVerified = bool.TryParse(emailVerifiedStr, out var v) && v;
            var name = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value
                       ?? jwt.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value
                       ?? principal.FindFirst(ClaimTypes.Name)?.Value;

            _logger.LogInformation("External token validated successfully: Provider={Provider}, ProviderKey={ProviderKey}", provider, providerKey);
            return new ExternalUserInfo(provider, providerKey, email, name, emailVerified);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogError(ex, "External token validation failed for provider: {Provider}", provider);
            throw new InvalidOperationException("Invalid external token", ex);
        }
    }

    private async Task<(JsonWebKeySet, string)> GetJwksAsync(string authority, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(authority, out var cached) && cached.Expiry > DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("Using cached JWKS for authority: {Authority}", authority);
            return (cached.Jwks, cached.Issuer);
        }

        _logger.LogDebug("Fetching JWKS for authority: {Authority}", authority);

        var configUrl = authority.TrimEnd('/') + "/.well-known/openid-configuration";
        using var resp = await _httpClient.GetAsync(configUrl, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var jwksUri = doc.RootElement.GetProperty("jwks_uri").GetString()!;
        var issuer = doc.RootElement.GetProperty("issuer").GetString()!;

        _logger.LogDebug("OpenID config fetched. JWKS URI: {JwksUri}, Issuer: {Issuer}", jwksUri, issuer);

        using var jwksResp = await _httpClient.GetAsync(jwksUri, cancellationToken);
        jwksResp.EnsureSuccessStatusCode();
        var jwksJson = await jwksResp.Content.ReadAsStringAsync(cancellationToken);
        var jwks = new JsonWebKeySet(jwksJson);

        _cache[authority] = (DateTimeOffset.UtcNow.AddHours(1), jwks, issuer);
        _logger.LogInformation("JWKS cached for authority: {Authority} (expires: {Expiry})", authority, DateTimeOffset.UtcNow.AddHours(1));

        return (jwks, issuer);
    }
}
