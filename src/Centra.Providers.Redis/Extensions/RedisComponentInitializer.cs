using Centra.Components;
using Centra.Providers.Redis.Locks;
using Centra.Providers.Redis.Options;
using Centra.Providers.Redis.PubSub;
using Centra.Providers.Redis.State;
using Centra.Registry;
using Microsoft.Extensions.Options;

namespace Centra.Providers.Redis.Extensions;

public sealed class RedisComponentInitializer : IComponentInitializer
{
    private readonly RedisStateStoreDriver _stateDriver;
    private readonly RedisPubSubDriver _pubSubDriver;
    private readonly RedisDistributedLockDriver _lockDriver;
    private readonly IOptions<RedisProviderOptions> _options;

    public RedisComponentInitializer(
        RedisStateStoreDriver stateDriver,
        RedisPubSubDriver pubSubDriver,
        RedisDistributedLockDriver lockDriver,
        IOptions<RedisProviderOptions> options)
    {
        _stateDriver = stateDriver ?? throw new ArgumentNullException(nameof(stateDriver));
        _pubSubDriver = pubSubDriver ?? throw new ArgumentNullException(nameof(pubSubDriver));
        _lockDriver = lockDriver ?? throw new ArgumentNullException(nameof(lockDriver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Initialize(ComponentRegistry registry)
    {
        var opts = _options.Value;

        registry.RegisterStateStoreDriver(opts.DefaultStateStoreName, _stateDriver);
        registry.RegisterPubSubDriver(opts.DefaultPubSubName, _pubSubDriver);
        registry.RegisterLockDriver(opts.DefaultLockStoreName, _lockDriver);

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = opts.DefaultStateStoreName,
            Type = ComponentType.StateStore,
            Provider = "redis"
        });

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = opts.DefaultPubSubName,
            Type = ComponentType.PubSub,
            Provider = "redis"
        });

        registry.RegisterComponent(new ComponentDefinition
        {
            Name = opts.DefaultLockStoreName,
            Type = ComponentType.DistributedLock,
            Provider = "redis"
        });
    }
}
