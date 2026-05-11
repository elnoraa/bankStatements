namespace Statements.WebAPI.Services.Auth;

public sealed record AuthUser(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified,
    string PasswordHash);
