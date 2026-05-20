using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Moq.Protected;
using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Tests.Services.Auth;

public sealed class ExternalAuthValidatorTests
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock = new(MockBehavior.Strict);
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExternalAuthValidator> _logger;
    private readonly HttpClient _httpClient;

    public ExternalAuthValidatorTests()
    {
        var configData = new Dictionary<string, string?>
        {
            ["ExternalProviders:TestProvider:Authority"] = "https://auth.example.com",
            ["ExternalProviders:TestProvider:ClientId"] = "test-client-id",
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        _logger = Mock.Of<ILogger<ExternalAuthValidator>>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
    }

    [Fact]
    public async Task ValidateAsync_WithMissingProviderConfig_ThrowsInvalidOperationException()
    {
        var factoryMock = CreateHttpClientFactory();
        var sut = new ExternalAuthValidator(_configuration, factoryMock, _logger);

        var act = () => sut.ValidateAsync("NonExistentProvider", "some-token", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Provider configuration not found: NonExistentProvider");
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidToken_ThrowsInvalidOperationException()
    {
        // Setup OpenID connect discovery response
        SetupOpenIdConfigurationResponse("https://auth.example.com");
        // Setup JWKS response
        SetupJwksResponse();

        var factoryMock = CreateHttpClientFactory();
        var sut = new ExternalAuthValidator(_configuration, factoryMock, _logger);

        var act = () => sut.ValidateAsync("TestProvider", "invalid-token", CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ValidateAsync_WithValidToken_ReturnsExternalUserInfo()
    {
        // Generate a real self-signed token for validation
        var (token, signingKey) = GenerateTestToken("test-subject", "test@example.com", "Test User");

        // Setup OpenID connect discovery
        SetupOpenIdConfigurationResponse("https://auth.example.com");
        // Setup JWKS response with the signing key
        SetupJwksResponse(signingKey);

        var factoryMock = CreateHttpClientFactory();
        var sut = new ExternalAuthValidator(_configuration, factoryMock, _logger);

        var result = await sut.ValidateAsync("TestProvider", token, CancellationToken.None);

        result.Provider.Should().Be("TestProvider");
        result.ProviderKey.Should().Be("test-subject");
        result.Email.Should().Be("test@example.com");
        result.DisplayName.Should().Be("Test User");
    }

    private void SetupOpenIdConfigurationResponse(string authority)
    {
        var openIdConfig = $$"""
            {
                "issuer": "{{authority}}",
                "jwks_uri": "{{authority}}/.well-known/jwks.json"
            }
            """;

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(r =>
                    r.RequestUri!.ToString().Contains("openid-configuration")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(openIdConfig, Encoding.UTF8, "application/json")
            });
    }

    private void SetupJwksResponse()
    {
        var jwksJson = """{"keys": []}""";

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(r =>
                    r.RequestUri!.ToString().Contains("jwks.json")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(jwksJson, Encoding.UTF8, "application/json")
            });
    }

    private void SetupJwksResponse(RsaSecurityKey signingKey)
    {
        var rsaParams = signingKey.Rsa!.ExportParameters(false);

        var jwksJson = $$"""
            {
                "keys": [
                    {
                        "kty": "RSA",
                        "use": "sig",
                        "alg": "RS256",
                        "n": "{{Base64UrlEncode(rsaParams.Modulus!)}}",
                        "e": "{{Base64UrlEncode(rsaParams.Exponent!)}}"
                    }
                ]
            }
            """;

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(r =>
                    r.RequestUri!.ToString().Contains("jwks.json")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(jwksJson, Encoding.UTF8, "application/json")
            });
    }

    private IHttpClientFactory CreateHttpClientFactory()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(x => x.CreateClient("external-auth"))
            .Returns(_httpClient);
        return factoryMock.Object;
    }

    private static (string token, RsaSecurityKey signingKey) GenerateTestToken(string subject, string email, string name)
    {
        var signingKey = new RsaSecurityKey(RSA.Create(2048));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim("sub", subject),
            new Claim("email", email),
            new Claim("email_verified", "true"),
            new Claim("name", name),
        };

        var token = new JwtSecurityToken(
            issuer: "https://auth.example.com",
            audience: "test-client-id",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var handler = new JwtSecurityTokenHandler();
        return (handler.WriteToken(token), signingKey);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
