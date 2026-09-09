using Centra.Locks;
using Centra.PubSub;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Centra.Providers.Redis.PubSub;

/// <summary>
/// Defines a contract for Redis Streams publishing and consumer group processing loops.
/// </summary>
public interface IRedisStreamProcessor
{
    /// <summary>
    /// Publishes a payload and metadata envelope to a Redis Stream.
    /// </summary>
    ValueTask PublishToStreamAsync(
        IDatabase db,
        string streamKey,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the consumer group reading and dispatch loop for a Redis Stream subscription.
    /// </summary>
    Task RunStreamLoopAsync(
        IDatabase db,
        IDistributedLockProvider? lockProvider,
        string streamKey,
        string groupName,
        string consumerName,
        string pubSubName,
        string? deadLetterTopic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        ConsumerMode consumerMode,
        ILogger logger,
        CancellationToken cancellationToken);

    /// <summary>
    /// Processes a single stream entry, delegating to the subscriber handler and acknowledging the message.
    /// </summary>
    ValueTask ProcessStreamEntryAsync(
        IDatabase db,
        string streamKey,
        string groupName,
        string pubSubName,
        string? deadLetterTopic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        StreamEntry entry,
        ILogger logger,
        CancellationToken cancellationToken);
}
