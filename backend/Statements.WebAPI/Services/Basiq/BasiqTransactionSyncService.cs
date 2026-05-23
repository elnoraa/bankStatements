using Dapper;
using Microsoft.Extensions.Options;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Basiq;

public sealed class BasiqTransactionSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BasiqOptions _options;
    private readonly ILogger<BasiqTransactionSyncService> _logger;

    public BasiqTransactionSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<BasiqOptions> options,
        ILogger<BasiqTransactionSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.SyncCheckIntervalMinutes);

        _logger.LogInformation(
            "Basiq sync service started, checking every {Interval} min",
            interval.TotalMinutes);

        // Initial delay at startup to let the app fully initialize
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueConnectionsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Basiq sync cycle");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task ProcessDueConnectionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbExecutor>();
        var basiqService = scope.ServiceProvider.GetRequiredService<IBasiqService>();

        var dueConnections = await db.QueryAsync<(Guid UserId, Guid ConnectionId)>(
            new CommandDefinition(
                """
                SELECT bc.user_id AS UserId, bc.id AS ConnectionId
                FROM basiq_connections bc
                WHERE bc.sync_enabled = true
                AND bc.status = 'active'
                AND (
                    bc.last_sync_at IS NULL
                    OR bc.last_sync_at + (bc.sync_frequency_minutes || ' minutes')::interval <= NOW()
                )
                FOR UPDATE SKIP LOCKED
                """,
                cancellationToken: ct));

        foreach (var (userId, connectionId) in dueConnections)
        {
            try
            {
                var count = await basiqService.SyncTransactionsAsync(
                    userId, connectionId, ct);

                _logger.LogInformation(
                    "Basiq sync completed for connection {ConnectionId}: {Count} new transactions",
                    connectionId, count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Basiq sync failed for connection {ConnectionId}", connectionId);
            }
        }
    }
}
