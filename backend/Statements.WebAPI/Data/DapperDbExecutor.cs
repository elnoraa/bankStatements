using Dapper;

namespace Statements.WebAPI.Data;

/// <summary>
/// Dapper-based implementation of <see cref="IDbExecutor"/> that creates a new connection for each operation.
/// </summary>
public sealed class DapperDbExecutor : IDbExecutor
{
    private readonly IDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DapperDbExecutor"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory used to create database connections.</param>
    public DapperDbExecutor(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<T?> QueryFirstOrDefaultAsync<T>(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<T>(command);
    }

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T>(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<T>(command);
    }

    /// <inheritdoc />
    public async Task<T> QuerySingleAsync<T>(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<T>(command);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<T>> QueryAsync<T>(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<T>(command);
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(command);
    }
}
