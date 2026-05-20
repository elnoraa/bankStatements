namespace Statements.WebAPI.Services.Auth;

/// <summary>
/// Represents an application user. Uses explicit properties (not primary constructor)
/// for reliable Dapper materialization across all versions.
/// </summary>
public sealed record AuthUser
{
    /// <summary>
    /// The unique identifier for the user.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The user's email address.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// The user's display name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the user's email has been verified.
    /// </summary>
    public bool EmailVerified { get; init; }

    /// <summary>
    /// The BCrypt hash of the user's password. Null for externally-authenticated users.
    /// </summary>
    public string PasswordHash { get; init; } = string.Empty;

    /// <summary>
    /// The number of consecutive failed login attempts.
    /// </summary>
    public int FailedLoginAttempts { get; init; }

    /// <summary>
    /// The date and time until which the account is locked, if applicable.
    /// </summary>
    public DateTime? LockedUntil { get; init; }
}
