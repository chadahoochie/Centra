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
}
