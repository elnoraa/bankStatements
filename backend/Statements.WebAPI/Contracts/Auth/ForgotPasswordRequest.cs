using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Request to initiate a password reset for a user's account.
/// If the email exists, a reset link will be sent.
/// </summary>
public sealed record ForgotPasswordRequest
{
    /// <summary>
    /// The email address of the account to reset.
    /// </summary>
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [MaxLength(320)]
    public string Email { get; init; } = string.Empty;
}
