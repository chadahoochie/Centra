using System.Buffers.Binary;
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
    private readonly byte[] _sharedBody = new byte[SharedBodyLength];
    private ulong _nextDeliveryTag;

    /// <summary>
    /// Header carrying the delivery tag alongside the body. Basic properties are a fresh object per
    /// delivery, so a handler can use this to tell which body it was <em>supposed</em> to receive.
    /// </summary>
    public const string DeliveryTagHeader = "x-test-delivery-tag";

    private const int SharedBodyLength = 8;

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
    /// Dispatches <paramref name="count"/> deliveries that all share one body buffer, rewriting it in
    /// place before each hand-over. This is the client's documented contract for
    /// <c>ReceivedAsync</c>: the body memory is only valid until the callback returns, after which the
    /// client is free to recycle it. A consumer that defers its read onto the thread pool without
    /// copying therefore reads whatever the next delivery - or
    /// <see cref="EnqueueSharedBodyRecycle"/> - has since written there.
    /// </summary>
    public void EnqueueSharedBodyDeliveries(int count, string exchange, string routingKey)
    {
        for (var i = 0; i < count; i++)
        {
            var deliveryTag = Interlocked.Increment(ref _nextDeliveryTag);
            _work.Writer.TryWrite(() =>
            {
                BinaryPrimitives.WriteUInt64LittleEndian(_sharedBody, deliveryTag);
                var properties = new BasicProperties
                {
                    Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [DeliveryTagHeader] = deliveryTag.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                };

                return _consumer.HandleBasicDeliverAsync(
                    _consumerTag,
                    deliveryTag,
                    redelivered: false,
                    exchange: exchange,
                    routingKey: routingKey,
                    properties: properties,
                    body: _sharedBody);
            });
        }
    }

    /// <summary>
    /// Models the client releasing the shared body buffer back to its pool and something else
    /// overwriting it, which is the state any still-deferred read observes.
    /// </summary>
    public void EnqueueSharedBodyRecycle()
    {
        _work.Writer.TryWrite(() =>
        {
            Array.Fill(_sharedBody, (byte)0xFF);
            return Task.CompletedTask;
        });
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
