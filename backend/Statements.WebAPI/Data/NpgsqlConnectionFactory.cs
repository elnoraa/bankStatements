using System.Data;
using Npgsql;

namespace Statements.WebAPI.Data;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly ILogger<NpgsqlConnectionFactory> _logger;

    public NpgsqlConnectionFactory(IConfiguration configuration, ILogger<NpgsqlConnectionFactory> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        _logger = logger;
    }

    public IDbConnection CreateConnection()
    {
        _logger.LogDebug("Creating new database connection");
        var connection = new NpgsqlConnection(_connectionString);
        _logger.LogDebug("Database connection created");
        return connection;
    }
}
