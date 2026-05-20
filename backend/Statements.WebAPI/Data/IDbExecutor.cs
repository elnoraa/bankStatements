using Dapper;

namespace Statements.WebAPI.Data;

public interface IDbExecutor
{
    Task<T?> QueryFirstOrDefaultAsync<T>(CommandDefinition command);
    Task<T?> QuerySingleOrDefaultAsync<T>(CommandDefinition command);
    Task<T> QuerySingleAsync<T>(CommandDefinition command);
    Task<IEnumerable<T>> QueryAsync<T>(CommandDefinition command);
    Task<int> ExecuteAsync(CommandDefinition command);
}
