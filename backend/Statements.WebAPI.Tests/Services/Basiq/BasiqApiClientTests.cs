using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Statements.WebAPI.Services.Basiq.Tests;

public sealed class BasiqApiClientTests
{
    private readonly Mock<HttpMessageHandler> _httpHandlerMock = new();
    private readonly BasiqOptions _options;
    private readonly HttpClient _httpClient;
    private readonly BasiqApiClient _sut;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BasiqApiClientTests()
    {
        _options = new BasiqOptions
        {
            ApiKey = "test-api-key",
            ApiBaseUrl = "https://au-api.basiq.io"
        };

        _httpHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        _httpClient = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri(_options.ApiBaseUrl)
        };

        _sut = new BasiqApiClient(
            _httpClient,
            Options.Create(_options),
            Mock.Of<ILogger<BasiqApiClient>>());
    }

    [Fact]
    public async Task CreateUserAsync_WithValidEmail_ReturnsUserId()
    {
        var basiqUserId = Guid.NewGuid().ToString();

        var tokenResponse = new BasiqTokenResponse
        {
            access_token = "server-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        var userResponse = new BasiqUserResponse
        {
            type = "user",
            id = basiqUserId
        };

        SetupTokenResponse(tokenResponse);
        SetupJsonResponse(HttpMethod.Post, "/users", userResponse);

        var result = await _sut.CreateUserAsync("test@example.com", CancellationToken.None);

        result.Should().Be(basiqUserId);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachesToken_DoesNotCallApiAgainWithinWindow()
    {
        var tokenResponse = new BasiqTokenResponse
        {
            access_token = "server-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        // Only allow one token call; second call should use cache
        SetupTokenResponse(tokenResponse);

        // Use private token via a public method
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains("/users/fake/accounts")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(/*lang=json,strict*/ """{"type":"list","data":[]}""")
            });

        // First call gets token, second uses cache
        await _sut.GetAccountsAsync("fake", CancellationToken.None);
        await _sut.GetAccountsAsync("fake", CancellationToken.None);

        // Token endpoint should only be called once
        _httpHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/token"),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetTransactionsAsync_WithSinceParam_AppendsFilter()
    {
        var tokenResponse = new BasiqTokenResponse
        {
            access_token = "server-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        SetupTokenResponse(tokenResponse);

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.PathAndQuery.Contains("filter=transactionDate.ge:2024-01-01")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(/*lang=json,strict*/ """{"type":"list","data":[],"links":{"self":"/users/u1/transactions"}}""")
            });

        var result = await _sut.GetTransactionsAsync("u1", "2024-01-01", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsAsync_HandlesPagination_FollowsNextLinks()
    {
        var tokenResponse = new BasiqTokenResponse
        {
            access_token = "server-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        SetupTokenResponse(tokenResponse);

        // Page 1 with next link
        var page1 = /*lang=json,strict*/ """
        {
            "type":"list",
            "data":[
                {"type":"transaction","id":"txn1","attributes":{"description":"Payment","amount":"-10.00","currency":"AUD","transactionDate":"2024-01-15","classification":"debit","status":"active"}}
            ],
            "links":{"next":"/users/u1/transactions?offset=1"}
        }
        """;

        // Page 2 without next link
        var page2 = /*lang=json,strict*/ """
        {
            "type":"list",
            "data":[
                {"type":"transaction","id":"txn2","attributes":{"description":"Deposit","amount":"100.00","currency":"AUD","transactionDate":"2024-01-14","classification":"credit","status":"active"}}
            ],
            "links":{"self":"/users/u1/transactions?offset=1"}
        }
        """;

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.PathAndQuery == "/users/u1/transactions"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page1) });

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.PathAndQuery == "/users/u1/transactions?offset=1"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page2) });

        var result = await _sut.GetTransactionsAsync("u1", null, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].id.Should().Be("txn1");
        result[1].id.Should().Be("txn2");
    }

    [Fact]
    public async Task GetTransactionsAsync_OnHttpError_ThrowsInvalidOperationException()
    {
        var tokenResponse = new BasiqTokenResponse
        {
            access_token = "server-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        SetupTokenResponse(tokenResponse);

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Bad Request")
            });

        var act = () => _sut.GetTransactionsAsync("u1", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*400*");
    }

    [Fact]
    public async Task CreateUserAsync_WithEmptyApiKey_ThrowsInvalidOperationException()
    {
        var options = new BasiqOptions
        {
            ApiKey = "",
            ApiBaseUrl = "https://au-api.basiq.io"
        };

        var client = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri(options.ApiBaseUrl)
        };

        var sut = new BasiqApiClient(
            client,
            Options.Create(options),
            Mock.Of<ILogger<BasiqApiClient>>());

        var act = () => sut.CreateUserAsync("test@example.com", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*API key*");
    }

    private void SetupTokenResponse(BasiqTokenResponse tokenResponse)
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/token"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tokenResponse, JsonOptions))
            });
    }

    private void SetupJsonResponse(HttpMethod method, string path, object response)
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == method && r.RequestUri!.PathAndQuery == path),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response, JsonOptions))
            });
    }
}
