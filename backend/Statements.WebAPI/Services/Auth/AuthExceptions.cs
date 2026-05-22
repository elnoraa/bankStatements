namespace Statements.WebAPI.Services.Auth;

/// <summary>
/// Thrown when an authentication operation conflicts with existing data (e.g., duplicate email during registration).
/// </summary>
public sealed class AuthConflictException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthConflictException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the conflict.</param>
    public AuthConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when the user provides invalid credentials (wrong email or password).
/// </summary>
public sealed class AuthInvalidCredentialsException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthInvalidCredentialsException"/> class.
    /// </summary>
    public AuthInvalidCredentialsException() : base("Invalid email or password.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthInvalidCredentialsException"/> class with a custom message.
    /// </summary>
    /// <param name="message">The error message describing the invalid credentials.</param>
    public AuthInvalidCredentialsException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when a user account is temporarily locked due to too many failed login attempts.
/// </summary>
public sealed class AuthAccountLockedException : Exception
{
    /// <summary>
    /// Gets the date and time until which the account is locked.
    /// </summary>
    public DateTime LockedUntil { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthAccountLockedException"/> class.
    /// </summary>
    /// <param name="lockedUntil">The date and time when the account will be unlocked.</param>
    public AuthAccountLockedException(DateTime lockedUntil)
        : base($"Account is temporarily locked. Please try again after {lockedUntil:HH:mm} UTC.")
    {
        LockedUntil = lockedUntil;
    }
}
