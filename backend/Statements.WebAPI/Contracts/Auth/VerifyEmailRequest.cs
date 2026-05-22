using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Request to verify an email address using a verification token.
/// </summary>
/// <param name="Token">The email verification token received by the user.</param>
public sealed record VerifyEmailRequest
{
    /// <summary>
    /// The verification token sent to the user's email address.
    /// </summary>
    [Required(ErrorMessage = "Verification token is required.")]
    public string Token { get; init; } = string.Empty;
}
