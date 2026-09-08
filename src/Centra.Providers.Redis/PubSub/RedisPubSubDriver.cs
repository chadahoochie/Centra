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
    private const string EnvelopeField = "envelope";

    private readonly IConnectionMultiplexer _connection;
    private readonly RedisProviderOptions _options;
    private readonly ILogger<RedisPubSubDriver> _logger;
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly string _consumerName = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private readonly ConcurrentDictionary<string, Action<RedisChannel, RedisValue>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _streamSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    public RedisPubSubDriver(
        IConnectionMultiplexer connection,
        IOptions<RedisProviderOptions> options,
        ILogger<RedisPubSubDriver>? logger = null,
        IDistributedLockProvider? lockProvider = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options?.Value ?? new RedisProviderOptions();
        _logger = logger ?? NullLogger<RedisPubSubDriver>.Instance;
        _lockProvider = lockProvider;
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
            await PublishToStreamAsync(pubSubName, topic, payload, metadata, cancellationToken).ConfigureAwait(false);
            return;
        }

        var sub = _connection.GetSubscriber();
        var channel = BuildChannel(pubSubName, topic);

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
            await SubscribeViaStreamAsync(pubSubName, topic, handler, deadLetterTopic, options, cancellationToken).ConfigureAwait(false);
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
        var channel = BuildChannel(pubSubName, topic);
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
                    var dlChannel = BuildChannel(pubSubName, deadLetterTopic);
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
        var channel = BuildChannel(pubSubName, topic);

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

    private string BuildChannel(string pubSubName, string topic) =>
        $"{_options.KeyPrefix}pubsub:{pubSubName}:{topic}";

    private string BuildStreamKey(string pubSubName, string topic) =>
        $"{_options.KeyPrefix}pubsub-stream:{pubSubName}:{topic}";

    private async ValueTask PublishToStreamAsync(
        string pubSubName,
        string topic,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var db = _connection.GetDatabase();
        var streamKey = BuildStreamKey(pubSubName, topic);

        var envelope = new RedisMessageEnvelope(metadata, payload.ToArray());
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

        await db.StreamAddAsync(streamKey, EnvelopeField, envelopeBytes).ConfigureAwait(false);
    }

    private async ValueTask SubscribeViaStreamAsync(
        string pubSubName,
        string topic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        string? deadLetterTopic,
        PubSubSubscribeOptions? options,
        CancellationToken cancellationToken)
    {
        var db = _connection.GetDatabase();
        var streamKey = BuildStreamKey(pubSubName, topic);
        var groupName = $"{pubSubName}.{topic}.group";

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

        _ = RunStreamLoopAsync(db, streamKey, groupName, pubSubName, deadLetterTopic, handler, consumerMode, cts.Token);
    }

    private async Task RunStreamLoopAsync(
        IDatabase db,
        string streamKey,
        string groupName,
        string pubSubName,
        string? deadLetterTopic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        ConsumerMode consumerMode,
        CancellationToken cancellationToken)
    {
        IDistributedLock? heldLock = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (consumerMode == ConsumerMode.SingleActiveConsumer && _lockProvider is not null && heldLock is null)
                {
                    heldLock = await _lockProvider.TryAcquireLockAsync(
                        $"{pubSubName}-pubsub-lock",
                        $"{streamKey}:single-active",
                        TimeSpan.FromSeconds(30),
                        cancellationToken).ConfigureAwait(false);

                    if (heldLock is null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                StreamEntry[] entries;
                try
                {
                    entries = await db.StreamReadGroupAsync(streamKey, groupName, _consumerName, StreamPosition.NewMessages, count: 10).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error reading Redis stream {Stream}", streamKey);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (entries.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var entry in entries)
                {
                    await ProcessStreamEntryAsync(db, streamKey, groupName, pubSubName, deadLetterTopic, handler, entry, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on unsubscribe/shutdown.
        }
        finally
        {
            if (heldLock is not null)
            {
                await heldLock.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask ProcessStreamEntryAsync(
        IDatabase db,
        string streamKey,
        string groupName,
        string pubSubName,
        string? deadLetterTopic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        StreamEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelopeField = entry.Values.FirstOrDefault(v => v.Name == EnvelopeField);
            byte[] raw = envelopeField.Value.IsNullOrEmpty ? Array.Empty<byte>() : (byte[])envelopeField.Value!;
            var envelope = raw.Length > 0 ? JsonSerializer.Deserialize<RedisMessageEnvelope>(raw) : null;

            var payload = (ReadOnlyMemory<byte>)(envelope?.Payload ?? Array.Empty<byte>());
            var headers = envelope?.Headers ?? new Dictionary<string, string>();

            var result = await handler(payload, headers, cancellationToken).ConfigureAwait(false);

            if (result == EventHandlingResult.DeadLetter && !string.IsNullOrWhiteSpace(deadLetterTopic))
            {
                await PublishToStreamAsync(pubSubName, deadLetterTopic, payload, headers, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Redis stream entry {EntryId} on {Stream}", entry.Id, streamKey);
        }
        finally
        {
            await db.StreamAcknowledgeAsync(streamKey, groupName, entry.Id).ConfigureAwait(false);
        }
    }
}
