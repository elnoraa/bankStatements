using System.Data;
using Npgsql;

namespace Statements.WebAPI.Data;

/// <summary>
/// Creates Npgsql database connections using the configured connection string.
/// </summary>
public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly ILogger<NpgsqlConnectionFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlConnectionFactory"/> class.
    /// </summary>
    /// <param name="configuration">Application configuration containing the "DefaultConnection" connection string.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="InvalidOperationException">Thrown when the "DefaultConnection" string is not configured.</exception>
    public NpgsqlConnectionFactory(IConfiguration configuration, ILogger<NpgsqlConnectionFactory> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        _logger = logger;
    }

    /// <summary>
    /// Creates a new Npgsql database connection.
    /// </summary>
    /// <returns>An <see cref="IDbConnection"/> to the PostgreSQL database.</returns>
    public IDbConnection CreateConnection()
    {
        _logger.LogDebug("Creating new database connection");
        var connection = new NpgsqlConnection(_connectionString);
        _logger.LogDebug("Database connection created");
        return connection;
    }
}
