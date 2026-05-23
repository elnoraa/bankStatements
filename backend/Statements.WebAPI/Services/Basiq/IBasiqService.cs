using Statements.WebAPI.Contracts.Basiq;

namespace Statements.WebAPI.Services.Basiq;

public interface IBasiqService
{
    /// <summary>Initiates a new Basiq connection. Returns the consent UI URL.</summary>
    Task<InitiateConnectionResponse> InitiateConnectionAsync(
        Guid userId, InitiateConnectionRequest request, CancellationToken ct);

    /// <summary>Completes a connection after the user gives consent via the Basiq UI.
    /// Polls the job, creates bank accounts, activates the connection.</summary>
    Task<BasiqConnectionResponse> CompleteConnectionAsync(
        Guid userId, CompleteConnectionRequest request, CancellationToken ct);

    /// <summary>Lists the user's Basiq connections.</summary>
    Task<BasiqConnectionListResponse> GetConnectionsAsync(Guid userId, CancellationToken ct);

    /// <summary>Gets a single connection by ID.</summary>
    Task<BasiqConnectionResponse> GetConnectionAsync(
        Guid userId, Guid connectionId, CancellationToken ct);

    /// <summary>Updates sync configuration for a connection.</summary>
    Task<BasiqConnectionResponse> UpdateSyncConfigAsync(
        Guid userId, Guid connectionId, UpdateSyncConfigRequest request, CancellationToken ct);

    /// <summary>Triggers a manual sync of a connection.</summary>
    Task<BasiqConnectionResponse> RefreshConnectionAsync(
        Guid userId, Guid connectionId, CancellationToken ct);

    /// <summary>Removes a connection (stops future syncs, preserves imported data).</summary>
    Task RemoveConnectionAsync(Guid userId, Guid connectionId, CancellationToken ct);

    /// <summary>Gets sync history for a connection.</summary>
    Task<IReadOnlyList<SyncLogResponse>> GetSyncLogAsync(
        Guid userId, Guid connectionId, int limit, CancellationToken ct);

    /// <summary>Core sync logic: fetches from Basiq, deduplicates, inserts.
    /// Returns count of new transactions.</summary>
    Task<int> SyncTransactionsAsync(
        Guid userId, Guid connectionId, CancellationToken ct);
}
