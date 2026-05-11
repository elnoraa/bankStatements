using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

public sealed record LoginRequest(
    [property: Required, EmailAddress, MaxLength(320)] string Email,
    [property: Required, MinLength(8), MaxLength(200)] string Password);
