using Centra.Drivers;
using Centra.Providers.SqlServer.Locks;
using Centra.Providers.SqlServer.Options;
using Centra.Providers.SqlServer.State;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Providers.SqlServer.Extensions;

public static class CentraSqlServerServiceCollectionExtensions
{
    public static IServiceCollection AddCentraSqlServer(
        this IServiceCollection services,
        Action<SqlServerProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<SqlServerProviderOptions>();
        }

        services.TryAddSingleton<SqlServerStateStoreDriver>();
        services.TryAddSingleton<SqlServerDistributedLockDriver>();

        services.AddSingleton<IStateStoreDriver>(sp => sp.GetRequiredService<SqlServerStateStoreDriver>());
        services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<SqlServerDistributedLockDriver>());

        services.AddSingleton<IComponentInitializer, SqlServerComponentInitializer>();

        return services;
    }

    public static IServiceCollection AddCentraSqlServerStateStore(
        this IServiceCollection services,
        string storeName,
        Action<SqlServerProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<SqlServerProviderOptions>();
        }

        services.TryAddSingleton<SqlServerStateStoreDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<SqlServerStateStoreDriver>();
            return new SqlServerDelegateComponentInitializer(reg =>
            {
                reg.RegisterStateStoreDriver(storeName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = storeName,
                    Type = Components.ComponentType.StateStore,
                    Provider = "sqlserver"
                });
            });
        });

        return services;
    }

    public static IServiceCollection AddCentraSqlServerLocks(
        this IServiceCollection services,
        string lockStoreName,
        Action<SqlServerProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<SqlServerProviderOptions>();
        }

        services.TryAddSingleton<SqlServerDistributedLockDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<SqlServerDistributedLockDriver>();
            return new SqlServerDelegateComponentInitializer(reg =>
            {
                reg.RegisterLockDriver(lockStoreName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = lockStoreName,
                    Type = Components.ComponentType.DistributedLock,
                    Provider = "sqlserver"
                });
            });
        });

        return services;
    }
}
