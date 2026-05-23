namespace Statements.WebAPI.Services.Basiq;

public interface IBasiqApiClient
{
    /// <summary>Creates a Basiq user for the given email. Returns the Basiq user ID.</summary>
    Task<string> CreateUserAsync(string email, CancellationToken cancellationToken);

    /// <summary>Generates a short-lived client token for the consent UI.</summary>
    Task<string> GenerateClientTokenAsync(string basiqUserId, CancellationToken cancellationToken);

    /// <summary>Polls a job until completion and returns the final status.</summary>
    Task<BasiqJobResponse> GetJobAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>Lists all accounts for a Basiq user.</summary>
    Task<BasiqListResponse<BasiqAccountApiResponse>> GetAccountsAsync(
        string basiqUserId, CancellationToken cancellationToken);

    /// <summary>Fetches transactions with optional 'since' filter for incremental sync.
    /// Handles pagination automatically and aggregates all pages.</summary>
    Task<List<BasiqTransactionApiResponse>> GetTransactionsAsync(
        string basiqUserId, string? since, CancellationToken cancellationToken);
}
