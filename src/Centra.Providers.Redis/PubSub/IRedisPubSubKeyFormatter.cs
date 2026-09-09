namespace Centra.Providers.Redis.PubSub;

/// <summary>
/// Defines a contract for formatting Redis pub/sub channel names, stream keys, and consumer group identifiers.
/// </summary>
public interface IRedisPubSubKeyFormatter
{
    /// <summary>
    /// Builds the channel name for standard Redis pub/sub messaging.
    /// </summary>
    string BuildChannel(string keyPrefix, string pubSubName, string topic);

    /// <summary>
    /// Builds the stream key for Redis Streams messaging.
    /// </summary>
    string BuildStreamKey(string keyPrefix, string pubSubName, string topic);

    /// <summary>
    /// Builds the consumer group name for a stream subscription.
    /// </summary>
    string BuildConsumerGroup(string pubSubName, string topic);
}
