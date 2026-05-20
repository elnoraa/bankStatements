namespace Statements.WebAPI.Contracts.Auth;

public sealed class ExternalLoginRequest
{
    public string Provider { get; init; } = null!;
    public string IdToken { get; init; } = null!;
}
