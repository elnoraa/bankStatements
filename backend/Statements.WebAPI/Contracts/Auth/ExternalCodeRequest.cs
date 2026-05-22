using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Request model for exchanging an authorization code (PKCE flow) for tokens via an external provider.
/// </summary>
public sealed class ExternalCodeRequest
{
    /// <summary>
    /// The OAuth/OpenID provider name (e.g., "Google", "Microsoft").
    /// </summary>
    [Required]
    public string Provider { get; init; } = null!;

    /// <summary>
    /// The authorization code returned from the provider's redirect.
    /// </summary>
    [Required]
    public string Code { get; init; } = null!;

    /// <summary>
    /// The PKCE code verifier used during the authorization request.
    /// </summary>
    [Required]
    public string CodeVerifier { get; init; } = null!;

    /// <summary>
    /// The redirect URI registered with the provider for callback.
    /// </summary>
    [Required, Url]
    public string RedirectUri { get; init; } = null!;
}
