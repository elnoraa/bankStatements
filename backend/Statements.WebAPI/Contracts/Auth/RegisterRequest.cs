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
    /// </summary>
    [Required, MinLength(8), MaxLength(200)]
    public string Password { get; init; } = null!;
}
