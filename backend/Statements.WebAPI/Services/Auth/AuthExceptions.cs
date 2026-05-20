namespace Statements.WebAPI.Services.Auth;

public sealed class AuthConflictException : Exception
{
    public AuthConflictException(string message) : base(message)
    {
    }
}

public sealed class AuthInvalidCredentialsException : Exception
{
    public AuthInvalidCredentialsException() : base("Invalid email or password.")
    {
    }
}

public sealed class AuthAccountLockedException : Exception
{
    public DateTime LockedUntil { get; }

    public AuthAccountLockedException(DateTime lockedUntil)
        : base($"Account is temporarily locked. Please try again after {lockedUntil:HH:mm} UTC.")
    {
        LockedUntil = lockedUntil;
    }
}
