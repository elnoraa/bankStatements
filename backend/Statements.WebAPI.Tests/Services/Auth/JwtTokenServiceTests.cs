using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Tests.Services.Auth;

public sealed class JwtTokenServiceTests
{
    private static readonly JwtOptions ValidOptions = new()
    {
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        Secret = "ThisIsA256BitLongSecretKeyForTestingPurposes!!",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30
    };

    private static readonly AuthUser TestUser = new()
    {
        Id = Guid.NewGuid(),
        Email = "test@example.com",
        DisplayName = "Test User",
        EmailVerified = true,
        PasswordHash = "somehash"
    };

    [Fact]
    public void CreateAccessToken_WithValidUser_ReturnsTokenWithCorrectClaims()
    {
        var sut = CreateSut(ValidOptions);

        var result = sut.CreateAccessToken(TestUser);

        result.Token.Should().NotBeNullOrWhiteSpace();
        result.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.Token);

        token.Subject.Should().Be(TestUser.Id.ToString());
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == TestUser.Email);
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Name && c.Value == TestUser.DisplayName);
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
        token.Issuer.Should().Be(ValidOptions.Issuer);
        token.Audiences.Should().Contain(ValidOptions.Audience);
    }

    [Fact]
    public void CreateAccessToken_WithValidUser_ReturnsTokenWithCorrectExpiry()
    {
        var sut = CreateSut(ValidOptions);

        var result = sut.CreateAccessToken(TestUser);

        result.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(ValidOptions.AccessTokenMinutes), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateAccessToken_WithEmptySecret_ThrowsInvalidOperationException()
    {
        var options = new JwtOptions
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            Secret = "",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 30
        };
        var sut = CreateSut(options);

        var act = () => sut.CreateAccessToken(TestUser);

        act.Should().Throw<InvalidOperationException>().WithMessage("JWT secret is not configured.");
    }

    [Fact]
    public void CreateAccessToken_WithWhitespaceSecret_ThrowsInvalidOperationException()
    {
        var options = new JwtOptions
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            Secret = "   ",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 30
        };
        var sut = CreateSut(options);

        var act = () => sut.CreateAccessToken(TestUser);

        act.Should().Throw<InvalidOperationException>().WithMessage("JWT secret is not configured.");
    }

    private static JwtTokenService CreateSut(JwtOptions options)
    {
        var logger = Mock.Of<ILogger<JwtTokenService>>();
        return new JwtTokenService(Options.Create(options), logger);
    }
}
