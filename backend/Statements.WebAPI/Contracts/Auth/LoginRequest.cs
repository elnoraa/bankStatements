using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Request model for user login.
/// </summary>
public sealed class LoginRequest
{
    /// <summary>
    /// The user's email address. Must be a valid email format.
    /// </summary>
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; init; } = null!;

    /// <summary>
    /// The user's password. Minimum 8 characters, maximum 200 characters.
    /// </summary>
    [Required, MinLength(8), MaxLength(200)]
    public string Password { get; init; } = null!;
}
