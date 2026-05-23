namespace Statements.WebAPI.Contracts.Basiq;

public sealed class InitiateConnectionRequest
{
    public string InstitutionName { get; init; } = string.Empty;
    public string? AccountName { get; init; }
}
