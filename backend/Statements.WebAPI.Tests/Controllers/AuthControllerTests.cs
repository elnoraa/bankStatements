using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Contracts.Auth;
using Statements.WebAPI.Controllers.v1;
using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Tests.Controllers;

/// <summary>
/// Unit tests for the <see cref="AuthController"/>.
/// </summary>
public sealed class AuthControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly IConfiguration _configuration;
    private readonly AuthController _sut;
    private readonly DefaultHttpContext _httpContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthControllerTests"/> class.
    /// Sets up the controller, mocks, and HTTP context.
    /// </summary>
    public AuthControllerTests()
    {
        _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        _httpContext = new DefaultHttpContext();
        _httpContext.Request.Scheme = "http";
        _httpContext.Request.Host = new HostString("localhost");

        _sut = new AuthController(
            _authServiceMock.Object,
            _httpClientFactoryMock.Object,
            _configuration,
            Mock.Of<ILogger<AuthController>>());
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = _httpContext
        };
    }

    /// <summary>
    /// Sets a refresh token cookie on the HTTP request for testing.
    /// </summary>
    /// <param name="token">The refresh token value.</param>
    private void SetRefreshTokenCookie(string token)
    {
        _httpContext.Request.Headers["Cookie"] = $"refresh_token={token}";
    }

    /// <summary>
    /// Creates a test <see cref="AuthResponse"/> with predetermined values.
    /// </summary>
    /// <returns>A populated <see cref="AuthResponse"/> for use in test setups.</returns>
    private static AuthResponse CreateTestAuthResponse()
    {
        return new AuthResponse(
            "access-token-123",
            DateTimeOffset.UtcNow.AddMinutes(15),
            "refresh-token-abc",
            DateTimeOffset.UtcNow.AddDays(30),
            new AuthUserResponse(Guid.NewGuid(), "test@test.com", "Test User", true));
    }

    // ── Register ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a valid registration request returns 201 Created with tokens and a Set-Cookie header.
    /// </summary>
    [Fact]
    public async Task Register_WithValidRequest_ReturnsCreated()
    {
        var request = new RegisterRequest { Email = "test@test.com", DisplayName = "Test User", Password = "SecureP@ss1" };
        var authResponse = CreateTestAuthResponse();

        _authServiceMock
            .Setup(x => x.RegisterAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResponse);

        var result = await _sut.Register(request, CancellationToken.None);

        var createdResult = result.Result.Should().BeOfType<CreatedResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
        var cookieResponse = createdResult.Value.Should().BeOfType<CookieAuthResponse>().Subject;
        cookieResponse.AccessToken.Should().Be("access-token-123");
        cookieResponse.User.Email.Should().Be("test@test.com");

        _sut.Response.Headers.Should().ContainKey("Set-Cookie");
    }

    /// <summary>
    /// Verifies that registering with a duplicate email returns 409 Conflict.
    /// </summary>
    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        var request = new RegisterRequest { Email = "existing@test.com", DisplayName = "Existing", Password = "SecureP@ss1" };

        _authServiceMock
            .Setup(x => x.RegisterAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthConflictException("Email taken"));

        var result = await _sut.Register(request, CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    // ── Login ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a login with valid credentials returns 200 OK with tokens and a Set-Cookie header.
    /// </summary>
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOk()
    {
        var request = new LoginRequest { Email = "test@test.com", Password = "CorrectP@ss1" };
        var authResponse = CreateTestAuthResponse();

        _authServiceMock
            .Setup(x => x.LoginAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResponse);

        var result = await _sut.Login(request, CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        var cookieResponse = okResult.Value.Should().BeOfType<CookieAuthResponse>().Subject;
        cookieResponse.AccessToken.Should().Be("access-token-123");

        _sut.Response.Headers.Should().ContainKey("Set-Cookie");
    }

    /// <summary>
    /// Verifies that a login with invalid credentials returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var request = new LoginRequest { Email = "test@test.com", Password = "WrongP@ss1" };

        _authServiceMock
            .Setup(x => x.LoginAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthInvalidCredentialsException());

        var result = await _sut.Login(request, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    /// <summary>
    /// Verifies that a login attempt on a locked account returns 401 with the lockedUntil timestamp.
    /// </summary>
    [Fact]
    public async Task Login_WithLockedAccount_ReturnsUnauthorizedWithLockedUntil()
    {
        var request = new LoginRequest { Email = "test@test.com", Password = "AnyP@ss1" };
        var lockedUntil = DateTime.UtcNow.AddMinutes(15);

        _authServiceMock
            .Setup(x => x.LoginAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthAccountLockedException(lockedUntil));

        var result = await _sut.Login(request, CancellationToken.None);

        var unauthorized = result.Result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var val = unauthorized.Value!;
        var lockedUntilProp = val.GetType().GetProperty("lockedUntil")!.GetValue(val);
        lockedUntilProp.Should().Be(lockedUntil);
    }

    // ── Refresh ────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a valid refresh token cookie returns 200 OK with new tokens.
    /// </summary>
    [Fact]
    public async Task Refresh_WithValidCookie_ReturnsOk()
    {
        var authResponse = CreateTestAuthResponse();
        SetRefreshTokenCookie("valid-refresh-token");

        _authServiceMock
            .Setup(x => x.RefreshTokenAsync("valid-refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResponse);

        var result = await _sut.Refresh(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    /// <summary>
    /// Verifies that a missing refresh token cookie returns 401 Unauthorized without calling the service.
    /// </summary>
    [Fact]
    public async Task Refresh_WithMissingCookie_ReturnsUnauthorized()
    {
        var result = await _sut.Refresh(CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        _authServiceMock.Verify(x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that an invalid refresh token returns 401 and clears the cookie.
    /// </summary>
    [Fact]
    public async Task Refresh_WithInvalidToken_ReturnsUnauthorizedAndClearsCookie()
    {
        SetRefreshTokenCookie("invalid-token");

        _authServiceMock
            .Setup(x => x.RefreshTokenAsync("invalid-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthInvalidCredentialsException());

        var result = await _sut.Refresh(CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        _sut.Response.Headers.Should().ContainKey("Set-Cookie");
    }

    // ── Logout ─────────────────────────────────────────────────

    /// <summary>
    /// Verifies that logout with a cookie revokes the token and clears the cookie.
    /// </summary>
    [Fact]
    public async Task Logout_WithCookie_CallsRevokeAndClearsCookie()
    {
        SetRefreshTokenCookie("valid-token");

        _authServiceMock
            .Setup(x => x.RevokeTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.Logout(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _authServiceMock.Verify(x => x.RevokeTokenAsync("valid-token", It.IsAny<CancellationToken>()), Times.Once);
        _sut.Response.Headers.Should().ContainKey("Set-Cookie");
    }

    /// <summary>
    /// Verifies that logout without a cookie does not call the revoke service.
    /// </summary>
    [Fact]
    public async Task Logout_WithoutCookie_DoesNotCallRevoke()
    {
        var result = await _sut.Logout(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _authServiceMock.Verify(x => x.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── External ───────────────────────────────────────────────

    /// <summary>
    /// Verifies that a valid external login request returns 200 OK.
    /// </summary>
    [Fact]
    public async Task External_WithValidRequest_ReturnsOk()
    {
        var request = new ExternalLoginRequest { Provider = "google", IdToken = "valid-id-token" };
        var authResponse = CreateTestAuthResponse();

        _authServiceMock
            .Setup(x => x.ExternalLoginAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResponse);

        var result = await _sut.External(request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    /// <summary>
    /// Verifies that an invalid provider returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task External_WithInvalidProvider_ReturnsBadRequest()
    {
        var request = new ExternalLoginRequest { Provider = "invalid", IdToken = "bad-token" };

        _authServiceMock
            .Setup(x => x.ExternalLoginAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider not configured"));

        var result = await _sut.External(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Verify Email ───────────────────────────────────────────

    /// <summary>
    /// Verifies that a valid token returns 200 OK.
    /// </summary>
    [Fact]
    public async Task VerifyEmail_WithValidToken_ReturnsOk()
    {
        var request = new VerifyEmailRequest { Token = "valid-token" };

        _authServiceMock
            .Setup(x => x.VerifyEmailAsync("valid-token", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.VerifyEmail(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    /// <summary>
    /// Verifies that an invalid token returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task VerifyEmail_WithInvalidToken_ReturnsBadRequest()
    {
        var request = new VerifyEmailRequest { Token = "invalid-token" };

        _authServiceMock
            .Setup(x => x.VerifyEmailAsync("invalid-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid verification token."));

        var result = await _sut.VerifyEmail(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Forgot Password ────────────────────────────────────────

    /// <summary>
    /// Verifies that a valid email returns 200 OK (always returns success to prevent enumeration).
    /// </summary>
    [Fact]
    public async Task ForgotPassword_ReturnsOkAlways()
    {
        var request = new ForgotPasswordRequest { Email = "test@example.com" };

        var result = await _sut.ForgotPassword(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── Reset Password ─────────────────────────────────────────

    /// <summary>
    /// Verifies that a valid reset token and password returns 200 OK.
    /// </summary>
    [Fact]
    public async Task ResetPassword_WithValidToken_ReturnsOk()
    {
        var request = new ResetPasswordRequest { Token = "valid-token", NewPassword = "NewSecureP@ss1" };

        _authServiceMock
            .Setup(x => x.ResetPasswordAsync("valid-token", "NewSecureP@ss1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.ResetPassword(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    /// <summary>
    /// Verifies that an invalid reset token returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task ResetPassword_WithInvalidToken_ReturnsBadRequest()
    {
        var request = new ResetPasswordRequest { Token = "expired-token", NewPassword = "NewSecureP@ss1" };

        _authServiceMock
            .Setup(x => x.ResetPasswordAsync("expired-token", "NewSecureP@ss1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Reset token has expired."));

        var result = await _sut.ResetPassword(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
