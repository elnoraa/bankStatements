namespace Statements.WebAPI.Contracts.Basiq;

public sealed class CompleteConnectionRequest
{
    public string JobId { get; init; } = string.Empty;
    public Guid ConnectionId { get; init; }
}
