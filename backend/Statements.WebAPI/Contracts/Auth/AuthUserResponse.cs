namespace Statements.WebAPI.Contracts.Auth;

public sealed record AuthUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified);
