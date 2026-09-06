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
    private readonly ConcurrentDictionary<string, string> _consumerTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _publishChannel;
    private int _disposed;

    public RabbitMQPubSubDriver(
        IConnectionFactory connectionFactory,
        IOptions<RabbitMQProviderOptions> options,
        ILogger<RabbitMQPubSubDriver>? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options?.Value ?? new RabbitMQProviderOptions();
        _logger = logger ?? NullLogger<RabbitMQPubSubDriver>.Instance;
    }

    private async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
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

    private async ValueTask<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken)
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
        CancellationToken cancellationToken = default)
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

        var queueName = $"{_options.QueuePrefix}.{pubSubName}.{topic}";

        Dictionary<string, object?>? queueArgs = null;
        if (!string.IsNullOrWhiteSpace(deadLetterTopic))
        {
            queueArgs = new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = _options.ExchangeName,
                ["x-dead-letter-routing-key"] = deadLetterTopic
            };
        }

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: _options.ExchangeName,
            routingKey: topic,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                var headers = new Dictionary<string, string>();
                if (ea.BasicProperties?.Headers is not null)
                {
                    foreach (var (k, v) in ea.BasicProperties.Headers)
                    {
                        if (v is byte[] bytes)
                        {
                            headers[k] = Encoding.UTF8.GetString(bytes);
                        }
                        else if (v is not null)
                        {
                            headers[k] = v.ToString()!;
                        }
                    }
                }

                var result = await handler(ea.Body, headers, CancellationToken.None).ConfigureAwait(false);

                switch (result)
                {
                    case EventHandlingResult.Success:
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false).ConfigureAwait(false);
                        break;

                    case EventHandlingResult.Drop:
                        await channel.BasicRejectAsync(ea.DeliveryTag, requeue: false).ConfigureAwait(false);
                        break;

                    case EventHandlingResult.DeadLetter:
                        await channel.BasicRejectAsync(ea.DeliveryTag, requeue: false).ConfigureAwait(false);
                        break;

                    default:
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception thrown while processing RabbitMQ message on queue {Queue}", queueName);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
            }
        };

        var tag = await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var subKey = $"{pubSubName}:{topic}";
        _consumerTags[subKey] = tag;
    }

    public async ValueTask UnsubscribeAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var subKey = $"{pubSubName}:{topic}";
        if (_consumerTags.TryRemove(subKey, out var tag) && _publishChannel is not null)
        {
            await _publishChannel.BasicCancelAsync(tag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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
