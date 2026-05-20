using System.Data;

namespace Statements.WebAPI.Data;

/// <summary>
/// Factory for creating database connections.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates a new database connection.
    /// </summary>
    /// <returns>An <see cref="IDbConnection"/> to the database.</returns>
    IDbConnection CreateConnection();
}
