using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.Auth;

public sealed record RegisterRequest(
    [property: Required, EmailAddress, MaxLength(320)] string Email,
    [property: MaxLength(120), RegularExpression(@"^[a-zA-Z0-9\s\-'._]+$", ErrorMessage = "Display name can only contain letters, numbers, spaces, hyphens, apostrophes, dots, and underscores.")] string? DisplayName,
    [property: Required, MinLength(8), MaxLength(200)] string Password);
