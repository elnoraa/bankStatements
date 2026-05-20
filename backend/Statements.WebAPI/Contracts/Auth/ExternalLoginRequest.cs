using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Request model for external (OAuth/OpenID) login using an identity token.
/// </summary>
public sealed class ExternalLoginRequest
{
    /// <summary>
    /// The OAuth/OpenID provider name (e.g., "Google", "Microsoft").
    /// </summary>
    [Required]
    public string Provider { get; init; } = null!;

    /// <summary>
    /// The identity token (ID token) issued by the external provider.
    /// </summary>
    [Required]
    public string IdToken { get; init; } = null!;
}
