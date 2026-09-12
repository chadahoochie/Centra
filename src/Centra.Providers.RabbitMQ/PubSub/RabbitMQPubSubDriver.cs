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

public sealed class RabbitMQPubSubDriver : IPubSubDriver, IAsyncDisposable
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly RabbitMQProviderOptions _options;
    private readonly ILogger<RabbitMQPubSubDriver> _logger;
    private readonly ConcurrentDictionary<string, (IChannel Channel, string ConsumerTag, SemaphoreSlim? Limiter)> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly IRabbitMQHeaderExtractor _headerExtractor;
    private readonly IRabbitMQMessageAcknowledger _acknowledger;
    private IConnection? _connection;
    private IChannel? _publishChannel;
    private int _disposed;

    public RabbitMQPubSubDriver(
        IConnectionFactory connectionFactory,
        IOptions<RabbitMQProviderOptions> options,
        ILogger<RabbitMQPubSubDriver>? logger = null,
        IRabbitMQHeaderExtractor? headerExtractor = null,
        IRabbitMQMessageAcknowledger? acknowledger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options?.Value ?? new RabbitMQProviderOptions();
        _logger = logger ?? NullLogger<RabbitMQPubSubDriver>.Instance;
        _headerExtractor = headerExtractor ?? RabbitMQHeaderExtractor.Instance;
        _acknowledger = acknowledger ?? RabbitMQMessageAcknowledger.Instance;
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
            durable: !autoDelete,
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

        var limiter = maxConcurrency > 1 ? new SemaphoreSlim(maxConcurrency, maxConcurrency) : null;

        var consumer = new AsyncEventingBasicConsumer(channel);
        if (limiter is not null)
        {
            consumer.ReceivedAsync += async (_, ea) =>
            {
                await limiter.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessAndAckAsync(channel, queueName, ea, handler).ConfigureAwait(false);
                    }
                    finally
                    {
                        limiter.Release();
                    }
                });
            };
        }
        else
        {
            consumer.ReceivedAsync += async (_, ea) =>
            {
                await ProcessAndAckAsync(channel, queueName, ea, handler).ConfigureAwait(false);
            };
        }

        var tag = await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var subKey = $"{pubSubName}:{topic}";
        _subscriptions[subKey] = (channel, tag, limiter);
    }

    internal async Task ProcessAndAckAsync(
        IChannel channel,
        string queueName,
        BasicDeliverEventArgs ea,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler)
    {
        try
        {
            var headers = _headerExtractor.ExtractHeaders(ea.BasicProperties);
            var result = await handler(ea.Body, headers, CancellationToken.None).ConfigureAwait(false);
            await _acknowledger.AcknowledgeMessageAsync(channel, ea.DeliveryTag, result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception thrown while processing RabbitMQ message on queue {Queue}", queueName);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
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
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var (_, sub) in _subscriptions)
        {
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
        }
        _subscriptions.Clear();

        if (_publishChannel is not null)
        {
            await _publishChannel.CloseAsync().ConfigureAwait(false);
            _publishChannel.Dispose();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            _connection.Dispose();
        }

        _connectionLock.Dispose();
    }
}
