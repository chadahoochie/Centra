using Centra.Components;
using Centra.Drivers;
using Centra.Providers.PostgreSql.Extensions;
using Centra.Providers.PostgreSql.Locks;
using Centra.Providers.PostgreSql.State;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace Centra.Providers.PostgreSql.Tests.Unit.Extensions;

public sealed class PostgreSqlHostingExtensionsTests
{
    [Fact]
    public void AddCentraPostgreSql_Should_Register_Drivers_And_Components()
    {
        var services = new ServiceCollection();
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=postgres;Password=postgres");
        services.AddSingleton(dataSource);

        services.AddCentraPostgreSql(options =>
        {
            options.DefaultStateStoreName = "pg-state";
            options.DefaultLockStoreName = "pg-locks";
        });

        var provider = services.BuildServiceProvider();

        provider.GetService<PostgreSqlStateStoreDriver>().ShouldNotBeNull();
        provider.GetService<PostgreSqlDistributedLockDriver>().ShouldNotBeNull();
        provider.GetService<IStateStoreDriver>().ShouldNotBeNull();
        provider.GetService<IDistributedLockDriver>().ShouldNotBeNull();

        var initializer = provider.GetRequiredService<IComponentInitializer>();
        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        registry.GetStateStoreDriver("pg-state").ShouldNotBeNull();
        registry.GetLockDriver("pg-locks").ShouldNotBeNull();

        var stateDef = registry.GetComponent("pg-state");
        stateDef.ShouldNotBeNull();
        stateDef.Type.ShouldBe(ComponentType.StateStore);
        stateDef.Provider.ShouldBe("postgresql");

        var lockDef = registry.GetComponent("pg-locks");
        lockDef.ShouldNotBeNull();
        lockDef.Type.ShouldBe(ComponentType.DistributedLock);
        lockDef.Provider.ShouldBe("postgresql");
    }
}
