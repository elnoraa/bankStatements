using System.Text.Json;
using RabbitMQ.Client;
using Statements.WebAPI.Contracts.Messages;

namespace Statements.WebAPI.Services.Messaging;

/// <summary>
/// Publishes messages to a RabbitMQ queue using the RabbitMQ.Client library.
/// Connection is created once and reused (singleton-safe).
/// </summary>
public sealed class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IChannel? _channel;

    public RabbitMqPublisher(IConfiguration configuration)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration.GetValue<string>("RabbitMq:Host") ?? "localhost",
            UserName = configuration.GetValue<string>("RabbitMq:Username") ?? "guest",
            Password = configuration.GetValue<string>("RabbitMq:Password") ?? "guest",
        };

        _connection = factory.CreateConnectionAsync()
            .GetAwaiter().GetResult();
    }

    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            _channel ??= await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await _channel.QueueDeclareAsync(
                queue: "process-statement",
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var body = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType());
            await _channel.BasicPublishAsync(
                exchange: "",
                routingKey: "process-statement",
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _channelLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.CloseAsync();
        await _connection.CloseAsync();
    }
}
