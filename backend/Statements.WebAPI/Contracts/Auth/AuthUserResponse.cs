namespace Statements.WebAPI.Contracts.Auth;

/// <summary>
/// Public-facing user profile information returned after authentication.
/// </summary>
/// <param name="Id">The unique identifier for the user.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="DisplayName">The user's display name.</param>
/// <param name="EmailVerified">Indicates whether the user's email has been verified.</param>
public sealed record AuthUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified);
