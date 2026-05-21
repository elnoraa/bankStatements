namespace Statements.WebAPI.Services.Messaging;

/// <summary>
/// Publishes messages to the message broker for background processing.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message to the configured queue.
    /// </summary>
    /// <typeparam name="T">The message type (serialized as JSON).</typeparam>
    /// <param name="message">The message payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
}
