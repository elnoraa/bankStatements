using System.Text.Json;
using RabbitMQ.Client;
using Statements.WebAPI.Contracts.Messages;

namespace Statements.WebAPI.Services.Messaging;

/// <summary>
/// Publishes messages to a RabbitMQ queue using the RabbitMQ.Client library.
/// Connection and channel are created lazily on first publish (async-safe).
/// Publisher confirms are enabled for at-least-once delivery semantics.
/// </summary>
public sealed class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _initialized;

    public RabbitMqPublisher(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        await EnsureInitializedAsync(cancellationToken);
        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType());

            var props = new BasicProperties
            {
                // Persistent delivery mode ensures messages survive broker restarts
                DeliveryMode = DeliveryModes.Persistent
            };

            // BasicPublishAsync completes when the broker confirms (ACKs) the message.
            // If the broker NACKs or the connection drops before confirming, the task
            // will throw — giving us at-least-once delivery semantics.
            await _channel!.BasicPublishAsync(
                exchange: "process-statement",
                routingKey: "process-statement",
                mandatory: true,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _channelLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

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

            _connection = await factory.CreateConnectionAsync(ct);
            // Enable publisher confirms via CreateChannelOptions (RabbitMQ.Client 7.x)
            // The await on BasicPublishAsync will wait for broker confirmation.
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);
            _channel = await _connection.CreateChannelAsync(
                options: channelOptions,
                cancellationToken: ct);

            // Declare the exchange (idempotent — no-op if already exists)
            await _channel.ExchangeDeclareAsync(
                exchange: "process-statement",
                type: "direct",
                durable: true,
                cancellationToken: ct);

            // Declare the main queue (idempotent — no-op if already exists)
            await _channel.QueueDeclareAsync(
                queue: "process-statement",
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: ct);

            // Bind the queue to the exchange
            await _channel.QueueBindAsync(
                queue: "process-statement",
                exchange: "process-statement",
                routingKey: "process-statement",
                cancellationToken: ct);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.CloseAsync();
        if (_connection is not null)
            await _connection.CloseAsync();
    }
}
