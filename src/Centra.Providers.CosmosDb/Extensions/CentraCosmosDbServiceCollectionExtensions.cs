using Centra.Drivers;
using Centra.Providers.CosmosDb.Locks;
using Centra.Providers.CosmosDb.Options;
using Centra.Providers.CosmosDb.State;
using Centra.Registry;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Centra.Providers.CosmosDb.Extensions;

public static class CentraCosmosDbServiceCollectionExtensions
{
    public static IServiceCollection AddCentraCosmosDb(
        this IServiceCollection services,
        Action<CosmosDbProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<CosmosDbProviderOptions>();
        }

        services.TryAddSingleton<CosmosClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CosmosDbProviderOptions>>().Value;
            return new CosmosClient(opts.ConnectionString, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            });
        });

        services.TryAddSingleton<CosmosDbStateStoreDriver>();
        services.TryAddSingleton<CosmosDbDistributedLockDriver>();

        services.AddSingleton<IStateStoreDriver>(sp => sp.GetRequiredService<CosmosDbStateStoreDriver>());
        services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<CosmosDbDistributedLockDriver>());

        services.AddSingleton<IComponentInitializer, CosmosDbComponentInitializer>();

        return services;
    }

    public static IServiceCollection AddCentraCosmosDbStateStore(
        this IServiceCollection services,
        string storeName,
        Action<CosmosDbProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<CosmosDbProviderOptions>();
        }

        services.TryAddSingleton<CosmosClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CosmosDbProviderOptions>>().Value;
            return new CosmosClient(opts.ConnectionString);
        });

        services.TryAddSingleton<CosmosDbStateStoreDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<CosmosDbStateStoreDriver>();
            return new CosmosDbDelegateComponentInitializer(reg =>
            {
                reg.RegisterStateStoreDriver(storeName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = storeName,
                    Type = Components.ComponentType.StateStore,
                    Provider = "cosmosdb"
                });
            });
        });

        return services;
    }

    public static IServiceCollection AddCentraCosmosDbLocks(
        this IServiceCollection services,
        string lockStoreName,
        Action<CosmosDbProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<CosmosDbProviderOptions>();
        }

        services.TryAddSingleton<CosmosClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CosmosDbProviderOptions>>().Value;
            return new CosmosClient(opts.ConnectionString);
        });

        services.TryAddSingleton<CosmosDbDistributedLockDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<CosmosDbDistributedLockDriver>();
            return new CosmosDbDelegateComponentInitializer(reg =>
            {
                reg.RegisterLockDriver(lockStoreName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = lockStoreName,
                    Type = Components.ComponentType.DistributedLock,
                    Provider = "cosmosdb"
                });
            });
        });

        return services;
    }
}
