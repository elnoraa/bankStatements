using Dapper;

namespace Statements.WebAPI.Data;

/// <summary>
/// Executes Dapper database commands with automatic connection management.
/// </summary>
public interface IDbExecutor
{
    /// <summary>
    /// Executes a query and returns the first result, or default if no results are found.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="command">The Dapper command definition containing SQL and parameters.</param>
    /// <returns>The first matching result, or default of <typeparamref name="T"/> if none.</returns>
    Task<T?> QueryFirstOrDefaultAsync<T>(CommandDefinition command);

    /// <summary>
    /// Executes a query expecting zero or one result, returning default if none.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="command">The Dapper command definition containing SQL and parameters.</param>
    /// <returns>The single result, or default of <typeparamref name="T"/> if none.</returns>
    Task<T?> QuerySingleOrDefaultAsync<T>(CommandDefinition command);

    /// <summary>
    /// Executes a query expecting exactly one result.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="command">The Dapper command definition containing SQL and parameters.</param>
    /// <returns>The single result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no results or more than one result are returned.</exception>
    Task<T> QuerySingleAsync<T>(CommandDefinition command);

    /// <summary>
    /// Executes a query and returns the results as an enumerable collection.
    /// </summary>
    /// <typeparam name="T">The type of the results.</typeparam>
    /// <param name="command">The Dapper command definition containing SQL and parameters.</param>
    /// <returns>An enumerable collection of results.</returns>
    Task<IEnumerable<T>> QueryAsync<T>(CommandDefinition command);

    /// <summary>
    /// Executes a non-query SQL command (INSERT, UPDATE, DELETE).
    /// </summary>
    /// <param name="command">The Dapper command definition containing SQL and parameters.</param>
    /// <returns>The number of affected rows.</returns>
    Task<int> ExecuteAsync(CommandDefinition command);
}
