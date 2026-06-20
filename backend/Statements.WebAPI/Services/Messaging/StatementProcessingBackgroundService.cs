using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Statements.WebAPI.Contracts.Messages;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Services.Messaging;

/// <summary>
/// Background service that listens to the RabbitMQ "process-statement" queue
/// and delegates processing to <see cref="ProcessStatementConsumer"/>.
///
/// Reliability features:
/// - Dead letter exchange + queue for poison messages (after delivery-limit retries)
/// - Automatic connection recovery with topology recovery
/// - Outer retry loop for total broker outage
/// </summary>
public sealed class StatementProcessingBackgroundService : BackgroundService
{
    private const string MainQueue = "process-statement";
    private const string DeadLetterExchange = "process-statement.dlx";
    private const string DeadLetterQueue = "process-statement.dlq";
    private const string DeadLetterRoutingKey = "process-statement.dlq";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StatementProcessingBackgroundService> _logger;

    public StatementProcessingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<StatementProcessingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ consumer connection lost, reconnecting in 10 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _configuration.GetValue<string>("RabbitMq:Host") ?? "localhost",
            UserName = _configuration.GetValue<string>("RabbitMq:Username") ?? "guest",
            Password = _configuration.GetValue<string>("RabbitMq:Password") ?? "guest",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            TopologyRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(60),
            ContinuationTimeout = TimeSpan.FromSeconds(20)
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declare DLX exchange and DLQ (idempotent — managed by code so the DLQ
        // exists even before the RabbitMQ definitions file applies the policy)
        await channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchange,
            type: "direct",
            durable: true,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: DeadLetterQueue,
            exchange: DeadLetterExchange,
            routingKey: DeadLetterRoutingKey,
            cancellationToken: stoppingToken);

        // Declare the main queue (idempotent — no-op if already exists)
        // The DLX + delivery-limit is applied via RabbitMQ policy (definitions.json),
        // not queue arguments, so no queue deletion is needed.
        await channel.QueueDeclareAsync(
            queue: MainQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Process one message at a time
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            ProcessStatementMessage? message = null;
            try
            {
                message = JsonSerializer.Deserialize<ProcessStatementMessage>(ea.Body.Span);
                if (message is null)
                {
                    // Bad message — NACK without requeue, it will be dead-lettered
                    await channel.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ProcessStatementConsumer>();
                await processor.ConsumeAsync(message, stoppingToken);

                // Success — acknowledge
                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process statement {StatementId}", message?.StatementId);

                // NACK with requeue=true — the broker's delivery-limit policy (5)
                // will dead-letter the message to process-statement.dlx after
                // the limit is exceeded. This replaces the old infinite retry loop.
                await channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);

                // Brief delay to avoid tight requeue loops on transient failures
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            MainQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Statement processing background service started, listening on queue '{Queue}' " +
            "with DLQ '{DeadLetterQueue}' and delivery-limit policy",
            MainQueue, DeadLetterQueue);

        // Keep running until cancellation is requested
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
    }
}
