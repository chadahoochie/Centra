using Centra.Drivers;
using Centra.Providers.PostgreSql.Locks;
using Centra.Providers.PostgreSql.Options;
using Centra.Providers.PostgreSql.State;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Centra.Providers.PostgreSql.Extensions;

public static class CentraPostgreSqlServiceCollectionExtensions
{
    public static IServiceCollection AddCentraPostgreSql(
        this IServiceCollection services,
        Action<PostgreSqlProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<PostgreSqlProviderOptions>();
        }

        services.TryAddSingleton<NpgsqlDataSource>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PostgreSqlProviderOptions>>().Value;
            return NpgsqlDataSource.Create(opts.ConnectionString);
        });

        services.TryAddSingleton<PostgreSqlStateStoreDriver>();
        services.TryAddSingleton<PostgreSqlDistributedLockDriver>();

        services.AddSingleton<IStateStoreDriver>(sp => sp.GetRequiredService<PostgreSqlStateStoreDriver>());
        services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<PostgreSqlDistributedLockDriver>());

        services.AddSingleton<IComponentInitializer, PostgreSqlComponentInitializer>();

        return services;
    }

    public static IServiceCollection AddCentraPostgreSqlStateStore(
        this IServiceCollection services,
        string storeName,
        Action<PostgreSqlProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<PostgreSqlProviderOptions>();
        }

        services.TryAddSingleton<NpgsqlDataSource>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PostgreSqlProviderOptions>>().Value;
            return NpgsqlDataSource.Create(opts.ConnectionString);
        });

        services.TryAddSingleton<PostgreSqlStateStoreDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<PostgreSqlStateStoreDriver>();
            return new PostgreSqlDelegateComponentInitializer(reg =>
            {
                reg.RegisterStateStoreDriver(storeName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = storeName,
                    Type = Components.ComponentType.StateStore,
                    Provider = "postgresql"
                });
            });
        });

        return services;
    }

    public static IServiceCollection AddCentraPostgreSqlLocks(
        this IServiceCollection services,
        string lockStoreName,
        Action<PostgreSqlProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<PostgreSqlProviderOptions>();
        }

        services.TryAddSingleton<NpgsqlDataSource>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PostgreSqlProviderOptions>>().Value;
            return NpgsqlDataSource.Create(opts.ConnectionString);
        });

        services.TryAddSingleton<PostgreSqlDistributedLockDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<PostgreSqlDistributedLockDriver>();
            return new PostgreSqlDelegateComponentInitializer(reg =>
            {
                reg.RegisterLockDriver(lockStoreName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = lockStoreName,
                    Type = Components.ComponentType.DistributedLock,
                    Provider = "postgresql"
                });
            });
        });

        return services;
    }
}
