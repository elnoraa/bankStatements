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
