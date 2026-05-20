namespace Statements.WebAPI.Services.Auth;

/// <summary>
/// Represents an application user. Uses explicit properties (not primary constructor)
/// for reliable Dapper materialization across all versions.
/// </summary>
public sealed record AuthUser
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool EmailVerified { get; init; }
    public string PasswordHash { get; init; } = string.Empty;
    public int FailedLoginAttempts { get; init; }
    public DateTime? LockedUntil { get; init; }
}
