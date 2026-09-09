using Centra.PubSub;
using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Defines a contract for acknowledging or rejecting RabbitMQ messages according to <see cref="EventHandlingResult"/>.
/// </summary>
public interface IRabbitMQMessageAcknowledger
{
    /// <summary>
    /// Acknowledges, rejects, or nacks a message delivery based on the handling result.
    /// </summary>
    ValueTask AcknowledgeMessageAsync(IChannel channel, ulong deliveryTag, EventHandlingResult result);
}
