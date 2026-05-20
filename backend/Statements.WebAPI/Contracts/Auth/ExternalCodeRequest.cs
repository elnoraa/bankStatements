namespace Statements.WebAPI.Contracts.Auth;

public sealed class ExternalCodeRequest
{
    public string Provider { get; init; } = null!;
    public string Code { get; init; } = null!;
    public string CodeVerifier { get; init; } = null!;
    public string RedirectUri { get; init; } = null!;
}
