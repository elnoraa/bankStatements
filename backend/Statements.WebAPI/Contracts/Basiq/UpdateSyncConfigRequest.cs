namespace Statements.WebAPI.Contracts.Basiq;

public sealed class UpdateSyncConfigRequest
{
    public bool? SyncEnabled { get; init; }
    public int? SyncFrequencyMinutes { get; init; }
}
