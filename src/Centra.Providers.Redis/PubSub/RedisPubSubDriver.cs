using System.Collections.Concurrent;
using System.Text.Json;
using Centra.Drivers;
using Centra.Providers.Redis.Options;
using Centra.PubSub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Centra.Providers.Redis.PubSub;

public sealed class RedisPubSubDriver : IPubSubDriver
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisProviderOptions _options;
    private readonly ILogger<RedisPubSubDriver> _logger;
    private readonly ConcurrentDictionary<string, Action<RedisChannel, RedisValue>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    public RedisPubSubDriver(
        IConnectionMultiplexer connection,
        IOptions<RedisProviderOptions> options,
        ILogger<RedisPubSubDriver>? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options?.Value ?? new RedisProviderOptions();
        _logger = logger ?? NullLogger<RedisPubSubDriver>.Instance;
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

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

        var sub = _connection.GetSubscriber();
        var channel = BuildChannel(pubSubName, topic);
        var subKey = $"{pubSubName}:{topic}";

        if (_subscriptions.TryRemove(subKey, out var handler))
        {
            await sub.UnsubscribeAsync(RedisChannel.Literal(channel), handler).ConfigureAwait(false);
        }
        else
        {
            await sub.UnsubscribeAsync(RedisChannel.Literal(channel)).ConfigureAwait(false);
        }
    }

    private string BuildChannel(string pubSubName, string topic) =>
        $"{_options.KeyPrefix}pubsub:{pubSubName}:{topic}";
}
