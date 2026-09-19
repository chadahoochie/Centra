using Centra.PubSub;
using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Default implementation of <see cref="IRabbitMQMessageAcknowledger"/>.
/// </summary>
public sealed class RabbitMQMessageAcknowledger : IRabbitMQMessageAcknowledger
{
    /// <summary>
    /// Singleton default instance of <see cref="RabbitMQMessageAcknowledger"/>.
    /// </summary>
    public static readonly RabbitMQMessageAcknowledger Instance = new();

    public ValueTask AcknowledgeMessageAsync(IChannel channel, ulong deliveryTag, EventHandlingResult result)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return result switch
        {
            EventHandlingResult.Success => channel.BasicAckAsync(deliveryTag, multiple: false),
            EventHandlingResult.Drop or EventHandlingResult.DeadLetter => channel.BasicRejectAsync(deliveryTag, requeue: false),
            _ => channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true)
        };
    }
}
