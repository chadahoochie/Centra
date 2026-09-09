namespace Centra.Providers.Redis.PubSub;

/// <summary>
/// Default implementation of <see cref="IRedisPubSubKeyFormatter"/>.
/// </summary>
public sealed class RedisPubSubKeyFormatter : IRedisPubSubKeyFormatter
{
    /// <summary>
    /// Singleton default instance of <see cref="RedisPubSubKeyFormatter"/>.
    /// </summary>
    public static readonly RedisPubSubKeyFormatter Instance = new();

    public string BuildChannel(string keyPrefix, string pubSubName, string topic) =>
        $"{keyPrefix}pubsub:{pubSubName}:{topic}";

    public string BuildStreamKey(string keyPrefix, string pubSubName, string topic) =>
        $"{keyPrefix}pubsub-stream:{pubSubName}:{topic}";

    public string BuildConsumerGroup(string pubSubName, string topic) =>
        $"{pubSubName}.{topic}.group";
}
