using System.Threading.Channels;
using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

/// <summary>
/// Models the RabbitMQ client's consumer dispatcher: a single serial work queue that hands buffered
/// deliveries to the consumer one at a time and orders cancel-ok behind every delivery already queued.
/// Lifecycle tests need that ordering to reproduce prefetched-but-not-yet-invoked deliveries, which a
/// test that calls <c>HandleBasicDeliverAsync</c> inline cannot produce.
/// </summary>
internal sealed class ConsumerDispatchQueue : IAsyncDisposable
{
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly IAsyncBasicConsumer _consumer;
    private readonly string _consumerTag;
    private readonly Task _pump;
    private ulong _nextDeliveryTag;

    public ConsumerDispatchQueue(IAsyncBasicConsumer consumer, string consumerTag)
    {
        _consumer = consumer;
        _consumerTag = consumerTag;
        _pump = Task.Run(async () =>
        {
            await foreach (var item in _work.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await item().ConfigureAwait(false);
            }
        });
    }

    public void EnqueueDeliveries(int count, string exchange, string routingKey)
    {
        for (var i = 0; i < count; i++)
        {
            var deliveryTag = Interlocked.Increment(ref _nextDeliveryTag);
            _work.Writer.TryWrite(() => _consumer.HandleBasicDeliverAsync(
                _consumerTag,
                deliveryTag,
                redelivered: false,
                exchange: exchange,
                routingKey: routingKey,
                properties: new BasicProperties(),
                body: ReadOnlyMemory<byte>.Empty));
        }
    }

    /// <summary>
    /// Completes once the pump has handed every delivery enqueued so far to the consumer callback,
    /// which is the handshake a test needs before asserting on what is in flight. The marker is a work
    /// item like any other, so the serial queue orders it behind exactly those deliveries.
    /// </summary>
    public Task HandedOverAsync()
    {
        var handedOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Writer.TryWrite(() =>
        {
            handedOver.TrySetResult();
            return Task.CompletedTask;
        });
        return handedOver.Task;
    }

    public void EnqueueCancelOk()
    {
        _work.Writer.TryWrite(() => _consumer.HandleBasicCancelOkAsync(_consumerTag));
        _work.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        _work.Writer.TryComplete();
        await _pump.ConfigureAwait(false);
    }
}
