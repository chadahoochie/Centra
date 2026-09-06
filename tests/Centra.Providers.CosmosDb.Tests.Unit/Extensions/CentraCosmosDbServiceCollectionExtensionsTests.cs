using Centra.Components;
using Centra.Drivers;
using Centra.Providers.CosmosDb.Extensions;
using Centra.Providers.CosmosDb.Locks;
using Centra.Providers.CosmosDb.Options;
using Centra.Providers.CosmosDb.State;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.CosmosDb.Tests.Unit.Extensions;

public sealed class CentraCosmosDbServiceCollectionExtensionsTests
{
    private const string ValidConnStr = "AccountEndpoint=https://fake.documents.azure.com:443/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;";

    [Fact]
    public async Task AddCentraCosmosDb_Should_Register_All_Required_Services_And_Initializers()
    {
        var services = new ServiceCollection();
        services.AddCentraCosmosDb(opts =>
        {
            opts.ConnectionString = ValidConnStr;
            opts.DatabaseName = "custom_db";
            opts.DefaultStateStoreName = "cosmos-state";
            opts.DefaultLockStoreName = "cosmos-locks";
        });

        await using var sp = services.BuildServiceProvider();

        var options = sp.GetService<IOptions<CosmosDbProviderOptions>>();
        options.ShouldNotBeNull();
        options.Value.DatabaseName.ShouldBe("custom_db");
        options.Value.DefaultStateStoreName.ShouldBe("cosmos-state");

        var stateDriver = sp.GetService<CosmosDbStateStoreDriver>();
        stateDriver.ShouldNotBeNull();

        var lockDriver = sp.GetService<CosmosDbDistributedLockDriver>();
        lockDriver.ShouldNotBeNull();

        var stateDriverInterface = sp.GetService<IStateStoreDriver>();
        stateDriverInterface.ShouldBeSameAs(stateDriver);

        var lockDriverInterface = sp.GetService<IDistributedLockDriver>();
        lockDriverInterface.ShouldBeSameAs(lockDriver);

        var initializer = sp.GetService<IComponentInitializer>();
        initializer.ShouldNotBeNull();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var store = registry.GetStateStoreDriver("cosmos-state");
        store.ShouldBeSameAs(stateDriver);

        var @lock = registry.GetLockDriver("cosmos-locks");
        @lock.ShouldBeSameAs(lockDriver);

        var compState = registry.GetComponent("cosmos-state");
        compState.ShouldNotBeNull();
        compState.Type.ShouldBe(ComponentType.StateStore);
        compState.Provider.ShouldBe("cosmosdb");

        var compLock = registry.GetComponent("cosmos-locks");
        compLock.ShouldNotBeNull();
        compLock.Type.ShouldBe(ComponentType.DistributedLock);
        compLock.Provider.ShouldBe("cosmosdb");
    }

    [Fact]
    public async Task AddCentraCosmosDbStateStore_Should_Register_Granular_State_Store()
    {
        var services = new ServiceCollection();
        services.AddCentraCosmosDbStateStore("products-store", opts =>
        {
            opts.ConnectionString = ValidConnStr;
        });

        await using var sp = services.BuildServiceProvider();
        var initializer = sp.GetService<IComponentInitializer>();
        initializer.ShouldNotBeNull();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var store = registry.GetStateStoreDriver("products-store");
        store.ShouldNotBeNull();

        var comp = registry.GetComponent("products-store");
        comp.ShouldNotBeNull();
        comp.Provider.ShouldBe("cosmosdb");
    }

    [Fact]
    public async Task AddCentraCosmosDbLocks_Should_Register_Granular_Lock_Store()
    {
        var services = new ServiceCollection();
        services.AddCentraCosmosDbLocks("products-lock", opts =>
        {
            opts.ConnectionString = ValidConnStr;
        });

        await using var sp = services.BuildServiceProvider();
        var initializer = sp.GetService<IComponentInitializer>();
        initializer.ShouldNotBeNull();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var @lock = registry.GetLockDriver("products-lock");
        @lock.ShouldNotBeNull();

        var comp = registry.GetComponent("products-lock");
        comp.ShouldNotBeNull();
        comp.Provider.ShouldBe("cosmosdb");
    }
}
