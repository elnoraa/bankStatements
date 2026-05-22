using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Request model for user registration.
/// </summary>
public sealed class RegisterRequest
{
    /// <summary>
    /// The user's email address. Must be a valid email format.
    /// </summary>
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; init; } = null!;

    /// <summary>
    /// Optional display name. Allows letters, numbers, spaces, hyphens, apostrophes, dots, and underscores.
    /// </summary>
    [MaxLength(120), RegularExpression(@"^[a-zA-Z0-9\s\-'._]+$", ErrorMessage = "Display name can only contain letters, numbers, spaces, hyphens, apostrophes, dots, and underscores.")]
    public string? DisplayName { get; init; }

    /// <summary>
    /// The user's password. Minimum 8 characters, maximum 200 characters.
    /// Must contain at least one uppercase letter, one lowercase letter, one digit, and one special character.
    /// </summary>
    [Required, MinLength(8), MaxLength(200)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).{8,200}$",
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one digit, and one special character.")]
    public string Password { get; init; } = null!;
}
