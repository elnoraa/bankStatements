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
/// - Multiple concurrent consumers within a single instance for throughput
/// </summary>
public sealed class StatementProcessingBackgroundService : BackgroundService
{
    private const string MainQueue = "process-statement";
    private const string MainExchange = "process-statement";
    private const string DeadLetterExchange = "process-statement.dlx";
    private const string DeadLetterQueue = "process-statement.dlq";
    private const string DeadLetterRoutingKey = "process-statement.dlq";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StatementProcessingBackgroundService> _logger;
    private readonly int _consumerCount;

    public StatementProcessingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<StatementProcessingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        _consumerCount = configuration.GetValue<int>("RabbitMq:ConsumerCount");
        if (_consumerCount <= 0)
            _consumerCount = 1; // safe default
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumersAsync(stoppingToken);
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

    /// <summary>
    /// Creates a single connection and spawns <see cref="_consumerCount"/> consumers,
    /// each on its own channel, all subscribing to the same queue.
    /// </summary>
    private async Task RunConsumersAsync(CancellationToken stoppingToken)
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

        var consumerTasks = new List<Task>();
        for (int i = 0; i < _consumerCount; i++)
        {
            var index = i;
            consumerTasks.Add(RunSingleConsumerAsync(connection, index, stoppingToken));
        }

        _logger.LogInformation(
            "Started {ConsumerCount} consumers on queue '{Queue}' with DLQ '{DeadLetterQueue}'",
            _consumerCount, MainQueue, DeadLetterQueue);

        await Task.WhenAll(consumerTasks);
    }

    /// <summary>
    /// Runs a single consumer on its own channel.
    /// All topology declarations are idempotent and safe to call from multiple channels.
    /// </summary>
    private async Task RunSingleConsumerAsync(
        IConnection connection, int consumerIndex, CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declare DLX exchange and DLQ (idempotent — no-op if already exists)
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

        // Declare main exchange and queue + binding (idempotent)
        await channel.ExchangeDeclareAsync(
            exchange: MainExchange,
            type: "direct",
            durable: true,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: MainQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: MainQueue,
            exchange: MainExchange,
            routingKey: MainQueue,
            cancellationToken: stoppingToken);

        // Allow each consumer to grab up to 3 messages at a time
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 3,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
            await HandleDeliveryAsync(channel, ea, stoppingToken);

        await channel.BasicConsumeAsync(
            MainQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogDebug("Consumer #{ConsumerIndex} started on queue '{Queue}'", consumerIndex, MainQueue);

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

    /// <summary>
    /// Handles a single message delivery: deserializes, delegates to <see cref="ProcessStatementConsumer"/>,
    /// and ACKs/NACKs the message accordingly.
    ///
    /// Internal visibility for unit testing.
    /// </summary>
    internal async Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        CancellationToken cancellationToken)
    {
        ProcessStatementMessage? message = null;
        try
        {
            message = JsonSerializer.Deserialize<ProcessStatementMessage>(ea.Body.Span);
            if (message is null)
            {
                // Bad message — NACK without requeue, it will be dead-lettered
                await channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<ProcessStatementConsumer>();
            await processor.ConsumeAsync(message, cancellationToken);

            // Success — acknowledge
            await channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process statement {StatementId}", message?.StatementId);

            // NACK with requeue=true — the broker's delivery-limit policy (5)
            // will dead-letter the message to process-statement.dlx after
            // the limit is exceeded. This replaces the old infinite retry loop.
            await channel.BasicNackAsync(ea.DeliveryTag, false, true, cancellationToken);

            // Brief delay to avoid tight requeue loops on transient failures
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
}
