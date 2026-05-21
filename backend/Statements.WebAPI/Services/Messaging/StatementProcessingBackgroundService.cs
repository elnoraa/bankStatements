using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Statements.WebAPI.Contracts.Messages;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Services.Messaging;

/// <summary>
/// Background service that listens to the RabbitMQ "process-statement" queue
/// and delegates processing to <see cref="ProcessStatementConsumer"/>.
/// </summary>
public sealed class StatementProcessingBackgroundService : BackgroundService
{
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
        var factory = new ConnectionFactory
        {
            HostName = _configuration.GetValue<string>("RabbitMq:Host") ?? "localhost",
            UserName = _configuration.GetValue<string>("RabbitMq:Username") ?? "guest",
            Password = _configuration.GetValue<string>("RabbitMq:Password") ?? "guest",
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: "process-statement",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

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
                    await channel.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ProcessStatementConsumer>();
                await processor.ConsumeAsync(message, stoppingToken);

                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process statement {StatementId}", message?.StatementId);
                // Requeue so the message isn't lost; will retry up to the queue's delivery limit
                await channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);
                // Brief delay to avoid tight re-queue loops on persistent failures
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        };

        await channel.BasicConsumeAsync("process-statement", autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        _logger.LogInformation("Statement processing background service started, listening on queue 'process-statement'");

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
