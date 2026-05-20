using System.Threading;

namespace Statements.WebAPI.Services.Auth;

/// <summary>
/// Information about a user retrieved from an external OAuth/OpenID provider.
/// </summary>
/// <param name="Provider">The name of the external provider (e.g., "Google", "Microsoft").</param>
/// <param name="ProviderKey">The unique user identifier from the provider.</param>
/// <param name="Email">The user's email address from the provider, if available.</param>
/// <param name="DisplayName">The user's display name from the provider, if available.</param>
/// <param name="EmailVerified">Indicates whether the provider has verified the user's email.</param>
public sealed record ExternalUserInfo(string Provider, string ProviderKey, string? Email, string? DisplayName, bool EmailVerified);

/// <summary>
/// Validates identity tokens issued by external OAuth/OpenID providers.
/// </summary>
public interface IExternalAuthValidator
{
    /// <summary>
    /// Validates an identity token from an external provider and extracts user information.
    /// </summary>
    /// <param name="provider">The provider name (e.g., "Google", "Microsoft").</param>
    /// <param name="idToken">The raw ID token to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="ExternalUserInfo"/> with the user's profile data from the provider.</returns>
    /// <exception cref="AuthUnauthorizedException">Thrown when the token is invalid or validation fails.</exception>
    Task<ExternalUserInfo> ValidateAsync(string provider, string idToken, CancellationToken cancellationToken);
}
