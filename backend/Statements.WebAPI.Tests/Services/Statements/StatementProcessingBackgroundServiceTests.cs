using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Statements.WebAPI.Contracts.Messages;
using Statements.WebAPI.Services.Messaging;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Tests.Services.Statements;

/// <summary>
/// Unit tests for <see cref="StatementProcessingBackgroundService.HandleDeliveryAsync"/>.
/// Tests the message handler logic without requiring a live RabbitMQ broker.
/// </summary>
public sealed class StatementProcessingBackgroundServiceTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IConfiguration> _configurationMock = new();
    private readonly Mock<ILogger<StatementProcessingBackgroundService>> _loggerMock = new();
    private readonly Mock<IServiceScope> _scopeMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<ProcessStatementConsumer> _consumerMock;
    private readonly Mock<IChannel> _channelMock = new();
    private readonly StatementProcessingBackgroundService _sut;

    public StatementProcessingBackgroundServiceTests()
    {
        // Build a partial mock of ProcessStatementConsumer so we can verify ConsumeAsync was called.
        // We need to mock its dependencies to avoid real DB calls.
        var dbExecutorMock = new Mock<Data.IDbExecutor>();
        var parserMock = new Mock<IStatementParser>();
        var ocrEngineMock = new Mock<IOCREngine>();
        var hubContextMock = new Mock<Microsoft.AspNetCore.SignalR.IHubContext<Hubs.StatementProcessingHub>>();
        var consumerLoggerMock = new Mock<ILogger<ProcessStatementConsumer>>();
        var configForConsumer = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:UploadsDirectory"] = Path.GetTempPath()
            })
            .Build();

        // Set up OCR engine to return null by default
        ocrEngineMock
            .Setup(x => x.ExtractTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult(null, false));

        _consumerMock = new Mock<ProcessStatementConsumer>(
            dbExecutorMock.Object,
            parserMock.Object,
            ocrEngineMock.Object,
            configForConsumer,
            hubContextMock.Object,
            consumerLoggerMock.Object)
        { CallBase = true };

        _scopeMock.Setup(x => x.ServiceProvider).Returns(_serviceProviderMock.Object);
        _serviceProviderMock
            .Setup(x => x.GetService(typeof(ProcessStatementConsumer)))
            .Returns(_consumerMock.Object);
        _serviceProviderMock
            .Setup(x => x.GetRequiredService(typeof(ProcessStatementConsumer)))
            .Returns(_consumerMock.Object);
        _scopeFactoryMock.Setup(x => x.CreateScope()).Returns(_scopeMock.Object);

        _configurationMock
            .Setup(x => x.GetValue<int>("RabbitMq:ConsumerCount"))
            .Returns(3);

        _sut = new StatementProcessingBackgroundService(
            _scopeFactoryMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    private static BasicDeliverEventArgs CreateDelivery(ProcessStatementMessage? message)
    {
        var body = message is not null
            ? JsonSerializer.SerializeToUtf8Bytes(message)
            : JsonSerializer.SerializeToUtf8Bytes(new { invalid = "data" });

        return new BasicDeliverEventArgs(
            consumerTag: "test-consumer",
            deliveryTag: 1,
            redelivered: false,
            exchange: "process-statement",
            routingKey: "process-statement",
            basicProperties: new BasicProperties(),
            body: body);
    }

    [Fact]
    public async Task HandleDeliveryAsync_ValidMessage_Acks()
    {
        var message = new ProcessStatementMessage
        {
            StatementId = Guid.NewGuid(),
            StoredFileName = "test.pdf",
            UserId = Guid.NewGuid(),
            BankAccountId = Guid.NewGuid()
        };
        var ea = CreateDelivery(message);

        // Set up consumer.ConsumeAsync to succeed (no throw)
        _consumerMock
            .Setup(x => x.ConsumeAsync(It.IsAny<ProcessStatementMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.HandleDeliveryAsync(_channelMock.Object, ea, CancellationToken.None);

        // Verify the consumer was called with the deserialized message
        _consumerMock.Verify(
            x => x.ConsumeAsync(It.Is<ProcessStatementMessage>(m => m.StatementId == message.StatementId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify ACK was sent
        _channelMock.Verify(
            x => x.BasicAckAsync(1, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleDeliveryAsync_NullMessage_NacksWithoutRequeue()
    {
        // A non-deserializable payload (the helper creates one with { "invalid": "data" })
        var ea = CreateDelivery(null);

        await _sut.HandleDeliveryAsync(_channelMock.Object, ea, CancellationToken.None);

        // Consumer should never be called
        _consumerMock.Verify(
            x => x.ConsumeAsync(It.IsAny<ProcessStatementMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // NACK with requeue=false (dead-letter)
        _channelMock.Verify(
            x => x.BasicNackAsync(1, false, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleDeliveryAsync_ConsumerThrows_NacksWithRequeue()
    {
        var message = new ProcessStatementMessage
        {
            StatementId = Guid.NewGuid(),
            StoredFileName = "test.pdf",
            UserId = Guid.NewGuid(),
            BankAccountId = Guid.NewGuid()
        };
        var ea = CreateDelivery(message);

        _consumerMock
            .Setup(x => x.ConsumeAsync(It.IsAny<ProcessStatementMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something went wrong"));

        await _sut.HandleDeliveryAsync(_channelMock.Object, ea, CancellationToken.None);

        // Verify consumer was called
        _consumerMock.Verify(
            x => x.ConsumeAsync(It.IsAny<ProcessStatementMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // NACK with requeue=true (broker will retry up to delivery-limit, then dead-letter)
        _channelMock.Verify(
            x => x.BasicNackAsync(1, false, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
