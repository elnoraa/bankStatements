namespace Statements.WebAPI.Auth;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly ILogger<BCryptPasswordHasher> _logger;

    public BCryptPasswordHasher(ILogger<BCryptPasswordHasher> logger)
    {
        _logger = logger;
    }

    public string Hash(string password)
    {
        _logger.LogDebug("Hashing password");
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        _logger.LogDebug("Password hashed successfully");
        return hash;
    }

    public bool Verify(string password, string passwordHash)
    {
        var result = BCrypt.Net.BCrypt.Verify(password, passwordHash);
        _logger.LogDebug("Password verification result: {Result}", result);
        return result;
    }
}
