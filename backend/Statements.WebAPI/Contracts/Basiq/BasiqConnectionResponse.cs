namespace Statements.WebAPI.Contracts.Basiq;

public sealed class BasiqConnectionResponse
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid? BankAccountId { get; init; }
    public string InstitutionName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool SyncEnabled { get; init; }
    public int SyncFrequencyMinutes { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public DateTimeOffset? LastSyncAt { get; init; }
    public string? ErrorMessage { get; init; }
}
