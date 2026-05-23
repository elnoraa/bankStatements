using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Statements.WebAPI.Services.Basiq;

public sealed class BasiqApiClient : IBasiqApiClient
{
    private const string ApiVersion = "3.0";
    private const string TokenEndpoint = "/token";
    private const string UsersEndpoint = "/users";
    private const string JobsEndpoint = "/jobs";

    private readonly HttpClient _httpClient;
    private readonly BasiqOptions _options;
    private readonly ILogger<BasiqApiClient> _logger;

    private static readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BasiqApiClient(
        HttpClient httpClient,
        IOptions<BasiqOptions> options,
        ILogger<BasiqApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // Set default headers for all requests
        _httpClient.DefaultRequestHeaders.Add("basiq-version", ApiVersion);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ─── Public API Methods ────────────────────────────────────────────────

    public async Task<string> CreateUserAsync(string email, CancellationToken ct)
    {
        _logger.LogInformation("---> POST /users body: {{ email: {Email} }}", email);

        var token = await GetServerAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, UsersEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { email });

        using var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("<--- POST /users {StatusCode} body: {Body}", (int)response.StatusCode, responseBody);

        await EnsureSuccessAsync(response, ct);

        var body = JsonSerializer.Deserialize<BasiqUserResponse>(responseBody, JsonOptions);
        if (body?.id is null)
            throw new InvalidOperationException("Basiq API returned empty user ID.");

        _logger.LogDebug("Created Basiq user: {BasiqUserId}", body.id);
        return body.id;
    }

    public async Task<string> GenerateClientTokenAsync(string basiqUserId, CancellationToken ct)
    {
        _logger.LogInformation("---> POST /token body: {{ scope: CLIENT_ACCESS, userId: {BasiqUserId} }}", basiqUserId);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException(
                "Basiq API key is not configured. Set the Basiq__ApiKey environment variable.");

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", _options.ApiKey.Trim());
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("scope", "CLIENT_ACCESS"),
            new KeyValuePair<string, string>("userId", basiqUserId)
        });

        using var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("<--- POST /token {StatusCode} body: {Body}", (int)response.StatusCode, SanitizeTokenBody(responseBody));

        await EnsureSuccessAsync(response, ct);

        var body = JsonSerializer.Deserialize<BasiqTokenResponse>(responseBody, JsonOptions);
        if (string.IsNullOrEmpty(body?.access_token))
            throw new InvalidOperationException("Basiq API returned empty client token.");

        return body.access_token;
    }

    public async Task<BasiqJobResponse> GetJobAsync(string jobId, CancellationToken ct)
    {
        return await SendGetAsync<BasiqJobResponse>($"{JobsEndpoint}/{jobId}", ct);
    }

    public async Task<BasiqListResponse<BasiqAccountApiResponse>> GetAccountsAsync(
        string basiqUserId, CancellationToken ct)
    {
        return await SendGetAsync<BasiqListResponse<BasiqAccountApiResponse>>(
            $"{UsersEndpoint}/{basiqUserId}/accounts", ct);
    }

    public async Task<List<BasiqTransactionApiResponse>> GetTransactionsAsync(
        string basiqUserId, string? since, CancellationToken ct)
    {
        var path = $"{UsersEndpoint}/{basiqUserId}/transactions";
        if (!string.IsNullOrEmpty(since))
        {
            path += $"?filter=transactionDate.ge:{since}";
        }

        _logger.LogInformation("---> GET {Path}", path);

        var allTransactions = new List<BasiqTransactionApiResponse>();
        var token = await GetServerAccessTokenAsync(ct);
        var pageCount = 0;

        while (!string.IsNullOrEmpty(path))
        {
            pageCount++;

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("<--- GET {Path} {StatusCode} body: {Body}", path, (int)response.StatusCode, responseBody);

            await EnsureSuccessAsync(response, ct);

            var page = JsonSerializer.Deserialize<BasiqListResponse<BasiqTransactionApiResponse>>(responseBody, JsonOptions);

            if (page?.data is not null)
                allTransactions.AddRange(page.data);

            path = page?.links?.next ?? string.Empty;

            _logger.LogInformation(
                "Page {Page}: {Count} transactions, next: {Next}",
                pageCount, page?.data.Count ?? 0, page?.links?.next ?? "(none)");
        }

        _logger.LogInformation(
            "Total: {Total} transactions across {Pages} pages for user {BasiqUserId} (since: {Since})",
            allTransactions.Count, pageCount, basiqUserId, since ?? "(all)");

        return allTransactions;
    }

    // ─── Token Management ──────────────────────────────────────────────────

    private async Task<string> GetServerAccessTokenAsync(CancellationToken ct)
    {
        // Fast path: cached token is still valid
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAt)
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAt)
                return _cachedToken;

            _logger.LogInformation("Requesting Basiq server access token");
            _logger.LogInformation("---> POST /token Authorization: Basic [REDACTED]");
            _logger.LogInformation("---> POST /token body: scope=SERVER_ACCESS");

            if (string.IsNullOrWhiteSpace(_options.ApiKey))
                throw new InvalidOperationException(
                    "Basiq API key is not configured. Set the Basiq__ApiKey environment variable.");

            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", _options.ApiKey.Trim());
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("scope", "SERVER_ACCESS")
            });

            using var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("<--- POST /token {StatusCode} body: {Body}", (int)response.StatusCode, SanitizeTokenBody(responseBody));

            await EnsureSuccessAsync(response, ct);

            var body = JsonSerializer.Deserialize<BasiqTokenResponse>(responseBody, JsonOptions);

            if (string.IsNullOrEmpty(body?.access_token))
                throw new InvalidOperationException("Basiq API returned empty access token.");

            _cachedToken = body.access_token;
            // Refresh 5 minutes before actual expiry (tokens expire at 60 min)
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(body.expires_in - 300, 60));

            _logger.LogInformation(
                "Basiq token obtained, expires at {ExpiresAt:AEST}",
                UtcToAest(_tokenExpiresAt));

            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private async Task<T> SendGetAsync<T>(string url, CancellationToken ct)
    {
        _logger.LogInformation("---> GET {Url}", url);

        var token = await GetServerAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("<--- GET {Url} {StatusCode} body: {Body}", url, (int)response.StatusCode, responseBody);

        await EnsureSuccessAsync(response, ct);

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Empty Basiq API response.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"Basiq API returned {(int)response.StatusCode}: {body}");
    }

    private static string SanitizeTokenBody(string json)
    {
        return Regex.Replace(json, "\"access_token\":\"[^\"]+\"", "\"access_token\":\"[REDACTED]\"");
    }

    private static DateTime UtcToAest(DateTime utc)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }
}
