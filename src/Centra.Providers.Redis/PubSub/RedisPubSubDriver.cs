using System.Collections.Concurrent;
using System.Text.Json;
using Centra.Drivers;
using Centra.Locks;
using Centra.Providers.Redis.Options;
using Centra.PubSub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Centra.Providers.Redis.PubSub;

public sealed class RedisPubSubDriver : IPubSubDriver, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisProviderOptions _options;
    private readonly ILogger<RedisPubSubDriver> _logger;
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly IRedisPubSubKeyFormatter _keyFormatter;
    private readonly IRedisStreamProcessor _streamProcessor;
    private readonly string _consumerName = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private readonly ConcurrentDictionary<string, Action<RedisChannel, RedisValue>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _streamSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    public RedisPubSubDriver(
        IConnectionMultiplexer connection,
        IOptions<RedisProviderOptions> options,
        ILogger<RedisPubSubDriver>? logger = null,
        IDistributedLockProvider? lockProvider = null,
        IRedisPubSubKeyFormatter? keyFormatter = null,
        IRedisStreamProcessor? streamProcessor = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options?.Value ?? new RedisProviderOptions();
        _logger = logger ?? NullLogger<RedisPubSubDriver>.Instance;
        _lockProvider = lockProvider;
        _keyFormatter = keyFormatter ?? RedisPubSubKeyFormatter.Instance;
        _streamProcessor = streamProcessor ?? RedisStreamProcessor.Instance;
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

        if (_options.EnableConsumerGroups)
        {
            var db = _connection.GetDatabase();
            var streamKey = _keyFormatter.BuildStreamKey(_options.KeyPrefix, pubSubName, topic);
            await _streamProcessor.PublishToStreamAsync(db, streamKey, payload, metadata, cancellationToken).ConfigureAwait(false);
            return;
        }

        var sub = _connection.GetSubscriber();
        var channel = _keyFormatter.BuildChannel(_options.KeyPrefix, pubSubName, topic);

        var envelope = new RedisMessageEnvelope(metadata, payload.ToArray());
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

        await sub.PublishAsync(RedisChannel.Literal(channel), envelopeBytes).ConfigureAwait(false);
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

        if (_options.EnableConsumerGroups)
        {
            await SubscribeViaStreamAsync(pubSubName, topic, handler, deadLetterTopic, cancellationToken, options).ConfigureAwait(false);
            return;
        }

        if (options?.ConsumerMode == ConsumerMode.SingleActiveConsumer)
        {
            _logger.LogWarning(
                "SingleActiveConsumer was requested for {PubSubName}/{Topic}, but RedisProviderOptions.EnableConsumerGroups is false, " +
                "so this subscription falls back to broadcast Pub/Sub and cannot honor single-active semantics.",
                pubSubName, topic);
        }

        var sub = _connection.GetSubscriber();
        var channel = _keyFormatter.BuildChannel(_options.KeyPrefix, pubSubName, topic);
        var subKey = $"{pubSubName}:{topic}";

        Action<RedisChannel, RedisValue> messageHandler = async (ch, val) =>
        {
            try
            {
                if (val.IsNullOrEmpty)
                {
                    return;
                }

                byte[] raw = val!;
                var envelope = JsonSerializer.Deserialize<RedisMessageEnvelope>(raw);
                if (envelope is null)
                {
                    return;
                }

                var payload = (ReadOnlyMemory<byte>)(envelope.Payload ?? Array.Empty<byte>());
                var headers = envelope.Headers ?? new Dictionary<string, string>();

                var result = await handler(payload, headers, CancellationToken.None).ConfigureAwait(false);

                if (result == EventHandlingResult.DeadLetter && !string.IsNullOrWhiteSpace(deadLetterTopic))
                {
                    var dlChannel = _keyFormatter.BuildChannel(_options.KeyPrefix, pubSubName, deadLetterTopic);
                    await sub.PublishAsync(RedisChannel.Literal(dlChannel), val).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Redis pub/sub message for topic {Topic}", topic);
            }
        };

        _subscriptions[subKey] = messageHandler;
        await sub.SubscribeAsync(RedisChannel.Literal(channel), messageHandler).ConfigureAwait(false);
    }

    public async ValueTask SubscribeViaStreamAsync(
        string pubSubName,
        string topic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        string? deadLetterTopic = null,
        CancellationToken cancellationToken = default,
        PubSubSubscribeOptions? options = null)
    {
        var db = _connection.GetDatabase();
        var streamKey = _keyFormatter.BuildStreamKey(_options.KeyPrefix, pubSubName, topic);
        var groupName = _keyFormatter.BuildConsumerGroup(pubSubName, topic);

        try
        {
            await db.StreamCreateConsumerGroupAsync(streamKey, groupName, StreamPosition.NewMessages, createStream: true).ConfigureAwait(false);
        }
        catch (RedisException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
            // Consumer group already exists - fine, another subscriber (or a prior run) created it.
        }

        var subKey = $"{pubSubName}:{topic}";
        var cts = new CancellationTokenSource();
        _streamSubscriptions[subKey] = cts;

        var consumerMode = options?.ConsumerMode ?? ConsumerMode.CompetingConsumer;
        var batchSize = options?.PrefetchCount is > 0 ? options.PrefetchCount.Value : _options.StreamBatchSize;

        _ = _streamProcessor.RunStreamLoopAsync(
            db,
            _lockProvider,
            streamKey,
            groupName,
            _consumerName,
            pubSubName,
            deadLetterTopic,
            handler,
            consumerMode,
            _logger,
            cts.Token,
            batchSize);
    }

    public async ValueTask UnsubscribeAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var subKey = $"{pubSubName}:{topic}";

        if (_streamSubscriptions.TryRemove(subKey, out var cts))
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        var sub = _connection.GetSubscriber();
        var channel = _keyFormatter.BuildChannel(_options.KeyPrefix, pubSubName, topic);

        if (_subscriptions.TryRemove(subKey, out var handler))
        {
            await sub.UnsubscribeAsync(RedisChannel.Literal(channel), handler).ConfigureAwait(false);
        }
        else if (!_options.EnableConsumerGroups)
        {
            await sub.UnsubscribeAsync(RedisChannel.Literal(channel)).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var (_, cts) in _streamSubscriptions)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _streamSubscriptions.Clear();

        return ValueTask.CompletedTask;
    }
}
