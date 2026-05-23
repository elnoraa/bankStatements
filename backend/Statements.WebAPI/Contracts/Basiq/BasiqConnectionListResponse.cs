namespace Statements.WebAPI.Contracts.Basiq;

public sealed class BasiqConnectionListResponse
{
    public IReadOnlyList<BasiqConnectionResponse> Connections { get; init; } = Array.Empty<BasiqConnectionResponse>();
}
