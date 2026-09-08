using StackExchange.Redis;

namespace Centra.Providers.Redis.Options;

public sealed class RedisProviderOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public ConfigurationOptions? ConfigurationOptions { get; set; }
    public string DefaultStateStoreName { get; set; } = "statestore";
    public string DefaultPubSubName { get; set; } = "pubsub";
    public string DefaultLockStoreName { get; set; } = "lockstore";
    public string KeyPrefix { get; set; } = "centra:";

    /// <summary>
    /// Opt-in: switches pub/sub from plain broadcast Pub/Sub (default, every subscribed process
    /// gets every message) to Redis Streams consumer groups, which is required for
    /// ConsumerMode.CompetingConsumer/SingleActiveConsumer to mean anything real. Off by default
    /// so existing consumers relying on today's broadcast semantics are not silently affected.
    /// </summary>
    public bool EnableConsumerGroups { get; set; } = false;
}
