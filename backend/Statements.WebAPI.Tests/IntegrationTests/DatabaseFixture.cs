using System.Data;
using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Statements.WebAPI.Tests.IntegrationTests;

/// <summary>
/// Dapper type handler for <see cref="DateOnly"/> mapping (copied from main project since the originals are internal).
/// </summary>
internal sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value)
    {
        if (value is DateOnly dateOnly)
            return dateOnly;
        return DateOnly.FromDateTime((DateTime)value);
    }

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }
}

/// <summary>
/// Dapper type handler for nullable <see cref="DateOnly"/> mapping.
/// </summary>
internal sealed class NullableDateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly?>
{
    public override DateOnly? Parse(object value)
    {
        if (value is null || value == DBNull.Value)
            return null;
        if (value is DateOnly dateOnly)
            return dateOnly;
        return DateOnly.FromDateTime((DateTime)value);
    }

    public override void SetValue(IDbDataParameter parameter, DateOnly? value)
    {
        if (value is null)
        {
            parameter.Value = DBNull.Value;
        }
        else
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.Value.ToDateTime(TimeOnly.MinValue);
        }
    }
}

/// <summary>
/// Manages a PostgreSQL Testcontainer for integration tests.
/// Starts a fresh container, runs the init SQL scripts, and disposes on teardown.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:13")
        .WithDatabase("bankstatements_test")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    /// <summary>
    /// Gets the connection string for the running PostgreSQL container.
    /// </summary>
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // In Docker-in-Docker (DinD), the Testcontainers connection string uses the
        // Docker gateway IP (e.g., 172.17.0.1) which is unreachable from inside the
        // test container. Replace it with host.docker.internal which resolves correctly
        // on Docker Desktop for Windows/Mac.
        if (Environment.GetEnvironmentVariable("DOCKER_HOST") != null)
        {
            var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
            builder.Host = "host.docker.internal";
            ConnectionString = builder.ConnectionString;
        }

        // Register Dapper type handlers (equivalent to those registered in Program.cs)
        // The originals are internal, so we register inline equivalents here.
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyTypeHandler());

        await InitializeDatabaseAsync();
    }

    /// <summary>
    /// Runs the database migration and seed SQL scripts to set up the schema.
    /// </summary>
    private async Task InitializeDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync();

        // Execute scripts in separate commands
        await connection.ExecuteAsync(TestSqlScripts.CreateTables, transaction: tx);
        await connection.ExecuteAsync(TestSqlScripts.SeedData, transaction: tx);

        await tx.CommitAsync();
    }

    /// <summary>
    /// Clears all data from the test tables, leaving the schema and seed categories intact.
    /// Call this between test classes that share the same container.
    /// </summary>
    public async Task ClearDataAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Disable triggers temporarily for clean truncation respecting FK constraints
        await connection.ExecuteAsync("""
            TRUNCATE TABLE
                refresh_tokens,
                statement_transactions,
                bank_statements,
                bank_accounts,
                external_logins,
                analysis_runs,
                app_users
            RESTART IDENTITY CASCADE
            """);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
