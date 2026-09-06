using Centra.Drivers;
using Centra.Providers.Redis.Locks;
using Centra.Providers.Redis.Options;
using Centra.Providers.Redis.PubSub;
using Centra.Providers.Redis.State;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Centra.Providers.Redis.Extensions;

public static class CentraRedisServiceCollectionExtensions
{
    public static IServiceCollection AddCentraRedis(
        this IServiceCollection services,
        Action<RedisProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RedisProviderOptions>();
        }

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RedisProviderOptions>>().Value;
            return opts.ConfigurationOptions is not null
                ? ConnectionMultiplexer.Connect(opts.ConfigurationOptions)
                : ConnectionMultiplexer.Connect(opts.ConnectionString);
        });

        services.TryAddSingleton<RedisStateStoreDriver>();
        services.TryAddSingleton<RedisPubSubDriver>();
        services.TryAddSingleton<RedisDistributedLockDriver>();

        services.AddSingleton<IStateStoreDriver>(sp => sp.GetRequiredService<RedisStateStoreDriver>());
        services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<RedisPubSubDriver>());
        services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<RedisDistributedLockDriver>());

        services.AddSingleton<IComponentInitializer, RedisComponentInitializer>();

        return services;
    }

    public static IServiceCollection AddCentraRedisStateStore(
        this IServiceCollection services,
        string storeName,
        Action<RedisProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RedisProviderOptions>();
        }

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RedisProviderOptions>>().Value;
            return opts.ConfigurationOptions is not null
                ? ConnectionMultiplexer.Connect(opts.ConfigurationOptions)
                : ConnectionMultiplexer.Connect(opts.ConnectionString);
        });

        services.TryAddSingleton<RedisStateStoreDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<RedisStateStoreDriver>();
            return new RedisDelegateComponentInitializer(reg =>
            {
                reg.RegisterStateStoreDriver(storeName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = storeName,
                    Type = Components.ComponentType.StateStore,
                    Provider = "redis"
                });
            });
        });

        return services;
    }

    public static IServiceCollection AddCentraRedisPubSub(
        this IServiceCollection services,
        string pubSubName,
        Action<RedisProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RedisProviderOptions>();
        }

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RedisProviderOptions>>().Value;
            return opts.ConfigurationOptions is not null
                ? ConnectionMultiplexer.Connect(opts.ConfigurationOptions)
                : ConnectionMultiplexer.Connect(opts.ConnectionString);
        });

        services.TryAddSingleton<RedisPubSubDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<RedisPubSubDriver>();
            return new RedisDelegateComponentInitializer(reg =>
            {
                reg.RegisterPubSubDriver(pubSubName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = pubSubName,
                    Type = Components.ComponentType.PubSub,
                    Provider = "redis"
                });
            });
        });

        return services;
    }

    public static IServiceCollection AddCentraRedisLocks(
        this IServiceCollection services,
        string lockStoreName,
        Action<RedisProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RedisProviderOptions>();
        }

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RedisProviderOptions>>().Value;
            return opts.ConfigurationOptions is not null
                ? ConnectionMultiplexer.Connect(opts.ConfigurationOptions)
                : ConnectionMultiplexer.Connect(opts.ConnectionString);
        });

        services.TryAddSingleton<RedisDistributedLockDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<RedisDistributedLockDriver>();
            return new RedisDelegateComponentInitializer(reg =>
            {
                reg.RegisterLockDriver(lockStoreName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = lockStoreName,
                    Type = Components.ComponentType.DistributedLock,
                    Provider = "redis"
                });
            });
        });

        return services;
    }
}
