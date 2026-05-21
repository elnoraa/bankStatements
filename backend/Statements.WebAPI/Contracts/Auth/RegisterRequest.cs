using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;
/// <summary>
/// Request model for user registration.
/// </summary>
/// <param name="Email">The user's email address. Must be a valid email format.</param>
/// <param name="DisplayName">Optional display name. Allows letters, numbers, spaces, hyphens, apostrophes, dots, and underscores.</param>
/// <param name="Password">The user's password. Minimum 8 characters, maximum 200 characters.</param>
public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [MaxLength(120), RegularExpression(@"^[a-zA-Z0-9\s\-'._]+$", ErrorMessage = "Display name can only contain letters, numbers, spaces, hyphens, apostrophes, dots, and underscores.")] string? DisplayName,
    [Required, MinLength(8), MaxLength(200)] string Password);
