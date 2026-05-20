using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Contracts.Auth;
using Statements.WebAPI.Controllers;
using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Tests.Controllers;

public sealed class AuthControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly IConfiguration _configuration;
    private readonly AuthController _sut;
    private readonly DefaultHttpContext _httpContext;

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

    private void SetRefreshTokenCookie(string token)
    {
        _httpContext.Request.Headers["Cookie"] = $"refresh_token={token}";
    }

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

    [Fact]
    public async Task Register_WithValidRequest_ReturnsCreated()
    {
        var request = new RegisterRequest("test@test.com", "Test User", "SecureP@ss1");
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

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        var request = new RegisterRequest("existing@test.com", "Existing", "SecureP@ss1");

        _authServiceMock
            .Setup(x => x.RegisterAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthConflictException("Email taken"));

        var result = await _sut.Register(request, CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    // ── Login ──────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOk()
    {
        var request = new LoginRequest("test@test.com", "CorrectP@ss1");
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

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var request = new LoginRequest("test@test.com", "WrongP@ss1");

        _authServiceMock
            .Setup(x => x.LoginAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthInvalidCredentialsException());

        var result = await _sut.Login(request, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_WithLockedAccount_ReturnsUnauthorizedWithLockedUntil()
    {
        var request = new LoginRequest("test@test.com", "AnyP@ss1");
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

    [Fact]
    public async Task Refresh_WithMissingCookie_ReturnsUnauthorized()
    {
        var result = await _sut.Refresh(CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        _authServiceMock.Verify(x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

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

    [Fact]
    public async Task Logout_WithoutCookie_DoesNotCallRevoke()
    {
        var result = await _sut.Logout(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _authServiceMock.Verify(x => x.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── External ───────────────────────────────────────────────

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
}
