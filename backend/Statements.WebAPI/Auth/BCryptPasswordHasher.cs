namespace Statements.WebAPI.Auth;

/// <summary>
/// Password hasher using BCrypt for secure password storage and verification.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly ILogger<BCryptPasswordHasher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BCryptPasswordHasher"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public BCryptPasswordHasher(ILogger<BCryptPasswordHasher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Hashes the specified password using BCrypt.
    /// </summary>
    /// <param name="password">The plain-text password to hash.</param>
    /// <returns>The BCrypt hash of the password.</returns>
    public string Hash(string password)
    {
        _logger.LogDebug("Hashing password");
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        _logger.LogDebug("Password hashed successfully");
        return hash;
    }

    /// <summary>
    /// Verifies a plain-text password against a BCrypt hash.
    /// </summary>
    /// <param name="password">The plain-text password to verify.</param>
    /// <param name="passwordHash">The stored BCrypt hash to compare against.</param>
    /// <returns><c>true</c> if the password matches the hash; otherwise <c>false</c>.</returns>
    public bool Verify(string password, string passwordHash)
    {
        var result = BCrypt.Net.BCrypt.Verify(password, passwordHash);
        _logger.LogDebug("Password verification result: {Result}", result);
        return result;
    }
}
