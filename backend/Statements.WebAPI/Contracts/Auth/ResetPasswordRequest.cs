using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Request to reset a password using a reset token.
/// </summary>
public sealed record ResetPasswordRequest
{
    /// <summary>
    /// The password reset token received by the user.
    /// </summary>
    [Required(ErrorMessage = "Reset token is required.")]
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// The new password for the account.
    /// </summary>
    [Required(ErrorMessage = "New password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    [MaxLength(200)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one digit, and one special character.")]
    public string NewPassword { get; init; } = string.Empty;
}
