using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

/// <summary>
/// Drives deliveries into a captured consumer the way the broker's dispatch loop would,
/// so lifecycle tests can put a known number of handlers in flight.
/// </summary>
internal sealed class ConsumerDeliverySource
{
    private readonly IAsyncBasicConsumer _consumer;
    private readonly string _consumerTag;
    private ulong _nextDeliveryTag;

    public ConsumerDeliverySource(IAsyncBasicConsumer consumer, string consumerTag)
    {
        _consumer = consumer;
        _consumerTag = consumerTag;
    }

    public async Task DeliverAsync(int count, string exchange, string routingKey)
    {
        for (var i = 0; i < count; i++)
        {
            await _consumer.HandleBasicDeliverAsync(
                _consumerTag,
                Interlocked.Increment(ref _nextDeliveryTag),
                redelivered: false,
                exchange: exchange,
                routingKey: routingKey,
                properties: new BasicProperties(),
                body: ReadOnlyMemory<byte>.Empty);
        }
    }
}
