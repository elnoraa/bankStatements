namespace Statements.WebAPI.Contracts.Basiq;

public sealed class InitiateConnectionResponse
{
    public Guid ConnectionId { get; init; }
    public string ConsentUrl { get; init; } = string.Empty;
    public string InstitutionName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}
