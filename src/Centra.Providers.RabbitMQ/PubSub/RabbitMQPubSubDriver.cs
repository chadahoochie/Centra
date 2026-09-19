using System.Collections.Concurrent;
using System.Text;
using Centra.Drivers;
using Centra.Providers.RabbitMQ.Options;
using Centra.PubSub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Centra.Providers.RabbitMQ.PubSub;

public sealed class RabbitMQPubSubDriver : IPubSubDriver, IPubSubQueueInspector, IPubSubShutdownDrain, IAsyncDisposable
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly RabbitMQProviderOptions _options;
    private readonly ILogger<RabbitMQPubSubDriver> _logger;
    private readonly ConcurrentDictionary<string, (IChannel Channel, string ConsumerTag, SemaphoreSlim? Limiter, CancellationTokenSource Shutdown, string QueueName)> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly IRabbitMQHeaderExtractor _headerExtractor;
    private readonly IRabbitMQMessageAcknowledger _acknowledger;
    private readonly IRabbitMQMessageIdentityReader _identityReader;
    private readonly IRedeliveryBudget _redeliveryBudget;
    private IConnection? _connection;
    private IChannel? _publishChannel;
    private ShutdownDrainWindow? _drainWindow;
    private int _disposed;

    public RabbitMQPubSubDriver(
        IConnectionFactory connectionFactory,
        IOptions<RabbitMQProviderOptions> options,
        ILogger<RabbitMQPubSubDriver>? logger = null,
        IRabbitMQHeaderExtractor? headerExtractor = null,
        IRabbitMQMessageAcknowledger? acknowledger = null,
        IRabbitMQMessageIdentityReader? identityReader = null,
        IRedeliveryBudget? redeliveryBudget = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options?.Value ?? new RabbitMQProviderOptions();
        _logger = logger ?? NullLogger<RabbitMQPubSubDriver>.Instance;
        _headerExtractor = headerExtractor ?? RabbitMQHeaderExtractor.Instance;
        _acknowledger = acknowledger ?? RabbitMQMessageAcknowledger.Instance;
        _identityReader = identityReader ?? RabbitMQMessageIdentityReader.Instance;
        _redeliveryBudget = redeliveryBudget ?? new BoundedRedeliveryBudget();
    }

    public async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken)
    {
        if (_publishChannel is not null)
        {
            return _publishChannel;
        }

        var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _publishChannel = channel;
        return _publishChannel;
    }

    public async ValueTask PublishAsync(
        string pubSubName,
        string topic,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var channel = await GetPublishChannelAsync(cancellationToken).ConfigureAwait(false);

        var props = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent
        };

        if (metadata.Count > 0)
        {
            var headers = new Dictionary<string, object?>();
            foreach (var (k, v) in metadata)
            {
                headers[k] = Encoding.UTF8.GetBytes(v);
            }
            props.Headers = headers;
        }

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: topic,
            mandatory: false,
            basicProperties: props,
            body: payload,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PublishBatchAsync(
        string pubSubName,
        string topic,
        IReadOnlyList<PubSubMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            return;
        }

        var channel = await GetPublishChannelAsync(cancellationToken).ConfigureAwait(false);

        foreach (var message in messages)
        {
            var props = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent
            };

            if (message.Metadata is not null && message.Metadata.Count > 0)
            {
                var headers = new Dictionary<string, object?>();
                foreach (var (k, v) in message.Metadata)
                {
                    headers[k] = Encoding.UTF8.GetBytes(v);
                }
                props.Headers = headers;
            }

            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: topic,
                mandatory: false,
                basicProperties: props,
                body: message.Payload,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask SubscribeAsync(
        string pubSubName,
        string topic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        string? deadLetterTopic = null,
        CancellationToken cancellationToken = default,
        PubSubSubscribeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        var redeliveryPolicy = RedeliveryBudgetPolicy.Resolve(options, _options);

        // A spent budget settles as reject-without-requeue, which the broker discards outright unless the
        // queue carries a dead-letter route. Queue arguments are fixed at declare time, so the route cannot
        // be retrofitted once the queue exists - refusing here is the only point where the caller can still
        // act on it, and it is strictly better than accepting the subscription and losing messages later.
        if (redeliveryPolicy.IsEnabled && string.IsNullOrWhiteSpace(deadLetterTopic))
        {
            throw new InvalidOperationException(
                $"Subscription '{pubSubName}/{topic}' asks for a redelivery budget of {redeliveryPolicy.MaxRetryAttempts}, "
                + "so a message whose budget is spent must be dead-lettered, but the queue would have no dead-letter route "
                + "and the broker would discard the message instead. Pass a non-empty deadLetterTopic to the same "
                + "SubscribeAsync call that set MaxRetryAttempts, or drop MaxRetryAttempts to leave the subscription "
                + "unbudgeted.");
        }

        var conn = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var prefetch = options?.PrefetchCount is > 0
            ? (ushort)options.PrefetchCount.Value
            : _options.DefaultPrefetchCount;

        if (prefetch > 0)
        {
            await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: prefetch,
                global: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var queueName = $"{_options.QueuePrefix}.{pubSubName}.{topic}";
        var autoDelete = options?.AutoDelete ?? false;

        Dictionary<string, object?>? queueArgs = null;
        if (!string.IsNullOrWhiteSpace(deadLetterTopic))
        {
            queueArgs ??= new Dictionary<string, object?>();
            queueArgs["x-dead-letter-exchange"] = _options.ExchangeName;
            queueArgs["x-dead-letter-routing-key"] = deadLetterTopic;
        }

        if (options?.ConsumerMode == ConsumerMode.SingleActiveConsumer)
        {
            // RabbitMQ enforces exclusivity server-side once this flag is set on the queue -
            // any number of consumers can attach, but only one is ever active at a time. Note:
            // queue arguments are fixed at declare time, so switching an existing topic's mode
            // requires deleting/recreating the queue (RabbitMQ throws PRECONDITION_FAILED
            // otherwise).
            queueArgs ??= new Dictionary<string, object?>();
            queueArgs["x-single-active-consumer"] = true;
        }

        if (options?.MessageTimeToLive is { } ttl && ttl > TimeSpan.Zero)
        {
            queueArgs ??= new Dictionary<string, object?>();
            queueArgs["x-message-ttl"] = (long)ttl.TotalMilliseconds;
        }

        if (options?.CustomArguments is { Count: > 0 } customArgs)
        {
            queueArgs ??= new Dictionary<string, object?>();
            foreach (var (k, v) in customArgs)
            {
                queueArgs[k] = v;
            }
        }

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: autoDelete,
            arguments: queueArgs,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: _options.ExchangeName,
            routingKey: topic,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var maxConcurrency = options?.MaxConcurrentCalls is > 0
            ? options.MaxConcurrentCalls.Value
            : _options.DefaultMaxConcurrentCalls;

        // Never disposed: SemaphoreSlim only needs disposal once AvailableWaitHandle has been accessed,
        // which this code never does, so disposing it on shutdown would buy nothing while opening a
        // window where the client's dispatcher can still call WaitAsync or Release on a disposed
        // semaphore. Do not reinstate a Dispose here.
        var limiter = maxConcurrency > 1 ? new SemaphoreSlim(maxConcurrency, maxConcurrency) : null;

        var shutdown = new CancellationTokenSource();
        var shutdownToken = shutdown.Token;

        var consumer = new AsyncEventingBasicConsumer(channel);

        // Completed by cancel-ok, which the consumer's serial work queue orders *after* every delivery
        // it had already buffered - the signal the drain waits on. The client raises the same event on
        // channel death, which RabbitMQSubscription separates out via the consumer's ShutdownReason.
        var consumerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.UnregisteredAsync += (_, _) =>
        {
            consumerCancelled.TrySetResult();
            return Task.CompletedTask;
        };

        if (limiter is not null)
        {
            consumer.ReceivedAsync += async (_, ea) =>
            {
                inFlight.BeginDelivery();
                await limiter.WaitAsync(CancellationToken.None).ConfigureAwait(false);

                // The client may recycle ea.Body the moment this callback returns, and it returns as
                // soon as the work is handed over - so the continuation reads an owned copy instead.
                // Basic properties are a per-delivery object, not pooled memory, so they travel as is.
                var body = new PooledDeliveryBody(ea.Body);
                var deliveryTag = ea.DeliveryTag;
                var properties = ea.BasicProperties;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessAndAckAsync(channel, queueName, ea, handler, redeliveryPolicy, shutdownToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        body.Return();
                        limiter.Release();
                        inFlight.CompleteDelivery();
                    }
                });
            };
        }
        else
        {
            consumer.ReceivedAsync += async (_, ea) =>
            {
                await ProcessAndAckAsync(channel, queueName, ea, handler, redeliveryPolicy, shutdownToken).ConfigureAwait(false);
            };
        }

        var tag = await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var subKey = $"{pubSubName}:{topic}";
        _subscriptions[subKey] = (channel, tag, limiter, shutdown, queueName);
    }

    /// <summary>
    /// Invokes the handler for one delivery and applies its acknowledgement decision. Takes the
    /// delivery's parts rather than the event args so a caller that had to copy the body out of the
    /// client's pooled buffer can pass its own copy.
    /// </summary>
    internal async Task ProcessAndAckAsync(
        IChannel channel,
        string queueName,
        BasicDeliverEventArgs ea,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        RedeliveryBudgetPolicy redeliveryPolicy,
        CancellationToken shutdownToken)
    {
        IReadOnlyDictionary<string, string>? headers = null;
        EventHandlingResult result;
        try
        {
            headers = _headerExtractor.ExtractHeaders(ea.BasicProperties);
            result = await handler(ea.Body, headers, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception thrown while processing RabbitMQ message on queue {Queue}", queueName);
            result = EventHandlingResult.Retry;
        }

        var messageId = _identityReader.ResolveIdentity(headers, ea.BasicProperties);
        var settlement = result;
        var backoff = TimeSpan.Zero;

        if (result is not EventHandlingResult.Retry)
        {
            if (messageId is not null)
            {
                _redeliveryBudget.Forget(new RedeliveryBudgetKey(queueName, messageId));
            }
        }
        else if (!redeliveryPolicy.IsEnabled)
        {
            // The subscription opted out of the budget, so Retry keeps its unbounded nack-requeue meaning.
        }
        else if (messageId is null)
        {
            // The broker cannot bound this loop: x-delivery-count only advances when a delivery is returned by
            // consumer or channel failure, never on an application nack-requeue. So a message carrying no identity
            // to count against would requeue forever, and dead-lettering it is the only bounded settlement left.
            _logger.LogWarning(
                "Retry requested for a message on queue {Queue} carrying neither ce-id nor message-id; "
                + "the redelivery budget cannot be enforced without a stable identity, so dead-lettering",
                queueName);

            settlement = EventHandlingResult.DeadLetter;
        }
        else
        {
            var decision = _redeliveryBudget.ChargeFailure(new RedeliveryBudgetKey(queueName, messageId), in redeliveryPolicy);
            settlement = decision.Result;
            backoff = decision.Delay;

            if (settlement is EventHandlingResult.DeadLetter)
            {
                _logger.LogWarning(
                    "Redelivery budget of {Budget} exhausted for message {MessageId} on queue {Queue}; dead-lettering",
                    redeliveryPolicy.MaxRetryAttempts,
                    messageId,
                    queueName);
            }
        }

        if (backoff > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(backoff, shutdownToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The subscription is draining. Leaving the delivery unacknowledged is the correct settlement:
                // the broker requeues it when the channel closes, and the alternative is holding the drain open
                // for the whole backoff.
                if (messageId is not null)
                {
                    _redeliveryBudget.Forget(new RedeliveryBudgetKey(queueName, messageId));
                }

                return;
            }
        }

        try
        {
            await _acknowledger.AcknowledgeMessageAsync(channel, ea.DeliveryTag, settlement).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Nothing above this frame can observe a failure here - the sequential path throws into the
            // RabbitMQ.Client dispatcher and the concurrent path onto an unobserved Task - and a channel closed
            // underneath us simply means the broker will redeliver.
            _logger.LogWarning(
                ex,
                "Failed to settle RabbitMQ delivery {DeliveryTag} on queue {Queue} as {Settlement}",
                ea.DeliveryTag,
                queueName,
                settlement);
        }
    }

    public async ValueTask UnsubscribeAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var subKey = $"{pubSubName}:{topic}";
        if (_subscriptions.TryRemove(subKey, out var sub))
        {
            await sub.Shutdown.CancelAsync().ConfigureAwait(false);

            try
            {
                await sub.Channel.BasicCancelAsync(sub.ConsumerTag, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error canceling consumer tag {Tag} on unsubscribe", sub.ConsumerTag);
            }

            try
            {
                await sub.Channel.CloseAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                sub.Channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing subscription channel on unsubscribe");
            }

            sub.Limiter?.Dispose();
            sub.Shutdown.Dispose();
            _redeliveryBudget.ForgetQueue(sub.QueueName);
        }
    }

    /// <inheritdoc />
    public IDisposable BeginShutdownDrain()
    {
        var window = new ShutdownDrainWindow(_options.TotalShutdownDrainTimeout);
        Volatile.Write(ref _drainWindow, window);
        return window;
    }

    public async ValueTask<PubSubQueueStats?> GetQueueStatsAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var queueName = $"{_options.QueuePrefix}.{pubSubName}.{topic}";
        try
        {
            var channel = await GetPublishChannelAsync(cancellationToken).ConfigureAwait(false);
            var declareOk = await channel.QueueDeclarePassiveAsync(queueName, cancellationToken).ConfigureAwait(false);
            return new PubSubQueueStats((long)declareOk.MessageCount, (int)declareOk.ConsumerCount);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not inspect queue stats for {QueueName}", queueName);
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // One budget for the whole disposal, not one per subscription: subscriptions are drained
        // sequentially, so a per-subscription timeout would multiply by the number of topics. A window
        // the host already opened is reused, because that is the same shutdown.
        var window = Volatile.Read(ref _drainWindow);
        if (window is not { IsOpen: true })
        {
            await sub.Shutdown.CancelAsync().ConfigureAwait(false);

            try
            {
                await sub.Channel.CloseAsync().ConfigureAwait(false);
                sub.Channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing subscription channel during disposal");
            }

            sub.Limiter?.Dispose();
            sub.Shutdown.Dispose();
            _redeliveryBudget.ForgetQueue(sub.QueueName);
        }
        _subscriptions.Clear();

        if (_publishChannel is not null)
        {
            try
            {
                await _publishChannel.CloseAsync().ConfigureAwait(false);
                _publishChannel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing publish channel during disposal");
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync().ConfigureAwait(false);
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing connection during disposal");
            }
        }

        _connectionLock.Dispose();
    }
}
