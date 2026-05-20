using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Statements.WebAPI.Contracts.Auth;
using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </summary>
    /// <param name="authService">Service for authentication operations.</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients (used for OAuth code exchange).</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public AuthController(
        IAuthService authService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    /// <param name="request">The registration details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CookieAuthResponse"/> with tokens and user info, plus a refresh token cookie.</returns>
    /// <response code="201">Registration successful.</response>
    /// <response code="409">Email already registered.</response>
    [HttpPost("register")]
    [EnableRateLimiting("AuthStrict")]
    public async Task<ActionResult<CookieAuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("POST /api/auth/register called");
        try
        {
            var response = await _authService.RegisterAsync(request, cancellationToken);
            _logger.LogInformation("Registration successful for user {UserId}", response.User.Id);

            SetRefreshTokenCookie(response.RefreshToken, response.RefreshTokenExpiresAt);

            var cookieResponse = new CookieAuthResponse(
                response.AccessToken,
                response.AccessTokenExpiresAt,
                response.User);

            return Created("/api/auth/register", cookieResponse);
        }
        catch (AuthConflictException exception)
        {
            _logger.LogWarning("Registration conflict: {Message}", exception.Message);
            return Conflict(exception.Message);
        }
    }

    /// <summary>
    /// Authenticates a user with email and password credentials.
    /// </summary>
    /// <param name="request">The login credentials.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CookieAuthResponse"/> with tokens and user info, plus a refresh token cookie.</returns>
    /// <response code="200">Login successful.</response>
    /// <response code="401">Invalid credentials or account locked.</response>
    [HttpPost("login")]
    [EnableRateLimiting("AuthStrict")]
    public async Task<ActionResult<CookieAuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("POST /api/auth/login called");
        try
        {
            var response = await _authService.LoginAsync(request, cancellationToken);
            _logger.LogInformation("Login successful for user {UserId}", response.User.Id);

            SetRefreshTokenCookie(response.RefreshToken, response.RefreshTokenExpiresAt);

            var cookieResponse = new CookieAuthResponse(
                response.AccessToken,
                response.AccessTokenExpiresAt,
                response.User);

            return Ok(cookieResponse);
        }
        catch (AuthAccountLockedException exception)
        {
            _logger.LogWarning("Login failed - account locked: {Message}", exception.Message);
            return Unauthorized(new { message = exception.Message, lockedUntil = exception.LockedUntil });
        }
        catch (AuthInvalidCredentialsException exception)
        {
            _logger.LogWarning("Login failed: {Message}", exception.Message);
            return Unauthorized(exception.Message);
        }
    }

    /// <summary>
    /// Refreshes the access token using the refresh token stored in the httpOnly cookie.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CookieAuthResponse"/> with new tokens, plus a rotated refresh token cookie.</returns>
    /// <response code="200">Token refreshed successfully.</response>
    /// <response code="401">Refresh token missing, invalid, or expired.</response>
    [HttpPost("refresh")]
    [EnableRateLimiting("AuthDefault")]
    public async Task<ActionResult<CookieAuthResponse>> Refresh(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("POST /api/auth/refresh called");

        var refreshToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            _logger.LogWarning("Refresh failed - no refresh token cookie");
            return Unauthorized("No refresh token provided.");
        }

        try
        {
            var response = await _authService.RefreshTokenAsync(refreshToken, cancellationToken);
            _logger.LogInformation("Token refreshed for user {UserId}", response.User.Id);

            SetRefreshTokenCookie(response.RefreshToken, response.RefreshTokenExpiresAt);

            var cookieResponse = new CookieAuthResponse(
                response.AccessToken,
                response.AccessTokenExpiresAt,
                response.User);

            return Ok(cookieResponse);
        }
        catch (AuthInvalidCredentialsException)
        {
            _logger.LogWarning("Refresh failed - invalid token");
            ClearRefreshTokenCookie();
            return Unauthorized("Invalid or expired refresh token.");
        }
    }

    /// <summary>
    /// Logs out the user by revoking the refresh token and clearing the httpOnly cookie.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK with a success message.</returns>
    [HttpPost("logout")]
    public async Task<ActionResult> Logout(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("POST /api/auth/logout called");

        var refreshToken = Request.Cookies["refresh_token"];
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await _authService.RevokeTokenAsync(refreshToken, cancellationToken);
        }

        ClearRefreshTokenCookie();
        _logger.LogInformation("Logout completed");
        return Ok(new { message = "Signed out successfully." });
    }

    /// <summary>
    /// Authenticates or registers a user via an external OAuth/OpenID identity token.
    /// </summary>
    /// <param name="request">The external login request with provider and ID token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CookieAuthResponse"/> with tokens and user info, plus a refresh token cookie.</returns>
    /// <response code="200">External login successful.</response>
    /// <response code="400">Invalid request or provider configuration.</response>
    [HttpPost("external")]
    [EnableRateLimiting("AuthDefault")]
    public async Task<ActionResult<CookieAuthResponse>> External(
        ExternalLoginRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("POST /api/auth/external called (provider: {Provider})", request.Provider);
        try
        {
            var response = await _authService.ExternalLoginAsync(request, cancellationToken);
            _logger.LogInformation("External login successful for user {UserId}", response.User.Id);

            SetRefreshTokenCookie(response.RefreshToken, response.RefreshTokenExpiresAt);

            var cookieResponse = new CookieAuthResponse(
                response.AccessToken,
                response.AccessTokenExpiresAt,
                response.User);

            return Ok(cookieResponse);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("External login failed: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Exchanges an authorization code (PKCE flow) for tokens from an external OAuth/OpenID provider.
    /// </summary>
    /// <param name="request">The external code request with provider, code, verifier, and redirect URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CookieAuthResponse"/> with tokens and user info, plus a refresh token cookie.</returns>
    /// <response code="200">Code exchange and login successful.</response>
    /// <response code="400">Invalid request, provider configuration, or token exchange failed.</response>
    [HttpPost("external/code")]
    [EnableRateLimiting("AuthModerate")]
    public async Task<ActionResult<CookieAuthResponse>> ExternalCode(
        ExternalCodeRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("POST /api/auth/external/code called (provider: {Provider})", request.Provider);
        try
        {
            var section = _configuration.GetSection($"ExternalProviders:{request.Provider}");
            if (!section.Exists())
            {
                _logger.LogWarning("Provider not configured: {Provider}", request.Provider);
                return BadRequest("Provider not configured");
            }

            var clientId = section.GetValue<string>("ClientId");
            var clientSecret = section.GetValue<string>("ClientSecret");
            var authority = section.GetValue<string>("Authority");
            if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId))
            {
                _logger.LogWarning("Invalid provider configuration for {Provider}", request.Provider);
                return BadRequest("Invalid provider configuration");
            }

            var http = _httpClientFactory.CreateClient("external-auth");

            // discover token endpoint
            _logger.LogDebug("Discovering token endpoint for {Provider}", request.Provider);
            var discoResp = await http.GetAsync(authority.TrimEnd('/') + "/.well-known/openid-configuration", cancellationToken);
            discoResp.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(await discoResp.Content.ReadAsStringAsync(cancellationToken));
            var tokenEndpoint = doc.RootElement.GetProperty("token_endpoint").GetString();
            _logger.LogDebug("Token endpoint discovered: {TokenEndpoint}", tokenEndpoint);

            var form = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "authorization_code"),
                new("code", request.Code),
                new("redirect_uri", request.RedirectUri),
                new("client_id", clientId),
                new("code_verifier", request.CodeVerifier)
            };

            if (!string.IsNullOrWhiteSpace(clientSecret))
            {
                form.Add(new KeyValuePair<string, string>("client_secret", clientSecret));
            }

            _logger.LogDebug("Exchanging authorization code for tokens");
            var tokenResp = await http.PostAsync(tokenEndpoint, new System.Net.Http.FormUrlEncodedContent(form), cancellationToken);

            if (!tokenResp.IsSuccessStatusCode)
            {
                var errorBody = await tokenResp.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Token endpoint returned {StatusCode} for {Provider}: {ErrorBody}",
                    (int)tokenResp.StatusCode, request.Provider, errorBody);
                tokenResp.EnsureSuccessStatusCode(); // will throw with status code
            }

            var tokenJson = await tokenResp.Content.ReadAsStringAsync(cancellationToken);
            using var tokenDoc = System.Text.Json.JsonDocument.Parse(tokenJson);

            if (!tokenDoc.RootElement.TryGetProperty("id_token", out var idTokenElement))
            {
                _logger.LogWarning("Token response did not contain id_token for {Provider}", request.Provider);
                return BadRequest("Token response did not contain id_token");
            }

            var idToken = idTokenElement.GetString()!;

            // delegate to existing flow that accepts an id_token
            var loginRequest = new ExternalLoginRequest { Provider = request.Provider, IdToken = idToken };
            var response = await _authService.ExternalLoginAsync(loginRequest, cancellationToken);
            _logger.LogInformation("External code login successful for user {UserId}", response.User.Id);

            SetRefreshTokenCookie(response.RefreshToken, response.RefreshTokenExpiresAt);

            var cookieResponse = new CookieAuthResponse(
                response.AccessToken,
                response.AccessTokenExpiresAt,
                response.User);

            return Ok(cookieResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External code login failed for provider: {Provider}", request.Provider);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Sets the refresh token as an httpOnly, SameSite=Strict cookie on the response.
    /// </summary>
    /// <param name="refreshToken">The refresh token value.</param>
    /// <param name="expiresAt">The cookie expiration time.</param>
    private void SetRefreshTokenCookie(string refreshToken, DateTimeOffset expiresAt)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            // In production behind HTTPS, this will be true. In dev over HTTP, it must be false for cookies to work.
            Secure = Request.IsHttps,
            Path = "/api/auth",
            IsEssential = true,
            Expires = expiresAt
        };

        Response.Cookies.Append("refresh_token", refreshToken, cookieOptions);
    }

    /// <summary>
    /// Clears the refresh token cookie by setting an expired empty cookie.
    /// </summary>
    private void ClearRefreshTokenCookie()
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            Path = "/api/auth",
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(-1)
        };

        Response.Cookies.Append("refresh_token", "", cookieOptions);
    }
}
