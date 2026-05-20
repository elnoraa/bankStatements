using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

public sealed class ExternalLoginRequest
{
    [Required]
    public string Provider { get; init; } = null!;

    [Required]
    public string IdToken { get; init; } = null!;
}
