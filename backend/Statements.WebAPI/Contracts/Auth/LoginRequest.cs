using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Request model for user login.
/// </summary>
/// <param name="Email">The user's email address. Must be a valid email format.</param>
/// <param name="Password">The user's password. Minimum 8 characters, maximum 200 characters.</param>
public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MinLength(8), MaxLength(200)] string Password);
