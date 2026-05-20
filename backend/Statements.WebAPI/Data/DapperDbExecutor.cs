using Dapper;

namespace Statements.WebAPI.Data;

public sealed class DapperDbExecutor : IDbExecutor
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DapperDbExecutor(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<T>(command);
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<T>(command);
    }

    public async Task<T> QuerySingleAsync<T>(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<T>(command);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<T>(command);
    }

    public async Task<int> ExecuteAsync(CommandDefinition command)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(command);
    }
}
