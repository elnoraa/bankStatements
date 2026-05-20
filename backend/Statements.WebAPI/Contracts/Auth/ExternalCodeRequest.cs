using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

public sealed class ExternalCodeRequest
{
    [Required]
    public string Provider { get; init; } = null!;

    [Required]
    public string Code { get; init; } = null!;

    [Required]
    public string CodeVerifier { get; init; } = null!;

    [Required]
    public string RedirectUri { get; init; } = null!;
}
