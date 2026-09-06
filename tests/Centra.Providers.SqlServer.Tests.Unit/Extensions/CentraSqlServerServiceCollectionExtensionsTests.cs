using Centra.Components;
using Centra.Drivers;
using Centra.Providers.SqlServer.Extensions;
using Centra.Providers.SqlServer.Locks;
using Centra.Providers.SqlServer.Options;
using Centra.Providers.SqlServer.State;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.SqlServer.Tests.Unit.Extensions;

public sealed class CentraSqlServerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCentraSqlServer_Should_Register_All_Required_Services_And_Initializers()
    {
        var services = new ServiceCollection();
        services.AddCentraSqlServer(opts =>
        {
            opts.ConnectionString = "Server=srv;Database=db;";
            opts.SchemaName = "custom";
        });

        using var sp = services.BuildServiceProvider();

        var options = sp.GetService<IOptions<SqlServerProviderOptions>>();
        options.ShouldNotBeNull();
        options.Value.ConnectionString.ShouldBe("Server=srv;Database=db;");
        options.Value.SchemaName.ShouldBe("custom");

        var stateDriver = sp.GetService<SqlServerStateStoreDriver>();
        stateDriver.ShouldNotBeNull();

        var lockDriver = sp.GetService<SqlServerDistributedLockDriver>();
        lockDriver.ShouldNotBeNull();

        var stateDriverInterface = sp.GetService<IStateStoreDriver>();
        stateDriverInterface.ShouldBeSameAs(stateDriver);

        var lockDriverInterface = sp.GetService<IDistributedLockDriver>();
        lockDriverInterface.ShouldBeSameAs(lockDriver);

        var initializer = sp.GetService<IComponentInitializer>();
        initializer.ShouldNotBeNull();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var store = registry.GetStateStoreDriver("statestore");
        store.ShouldBeSameAs(stateDriver);

        var @lock = registry.GetLockDriver("lockstore");
        @lock.ShouldBeSameAs(lockDriver);

        var compState = registry.GetComponent("statestore");
        compState.ShouldNotBeNull();
        compState.Type.ShouldBe(ComponentType.StateStore);
        compState.Provider.ShouldBe("sqlserver");

        var compLock = registry.GetComponent("lockstore");
        compLock.ShouldNotBeNull();
        compLock.Type.ShouldBe(ComponentType.DistributedLock);
        compLock.Provider.ShouldBe("sqlserver");
    }

    [Fact]
    public void AddCentraSqlServerStateStore_Should_Register_Granular_State_Store()
    {
        var services = new ServiceCollection();
        services.AddCentraSqlServerStateStore("orders-store", opts =>
        {
            opts.ConnectionString = "Server=srv;Database=db;";
        });

        using var sp = services.BuildServiceProvider();
        var initializer = sp.GetService<IComponentInitializer>();
        initializer.ShouldNotBeNull();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var store = registry.GetStateStoreDriver("orders-store");
        store.ShouldNotBeNull();

        var comp = registry.GetComponent("orders-store");
        comp.ShouldNotBeNull();
        comp.Provider.ShouldBe("sqlserver");
    }

    [Fact]
    public void AddCentraSqlServerLocks_Should_Register_Granular_Lock_Store()
    {
        var services = new ServiceCollection();
        services.AddCentraSqlServerLocks("orders-lock", opts =>
        {
            opts.ConnectionString = "Server=srv;Database=db;";
        });

        using var sp = services.BuildServiceProvider();
        var initializer = sp.GetService<IComponentInitializer>();
        initializer.ShouldNotBeNull();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var @lock = registry.GetLockDriver("orders-lock");
        @lock.ShouldNotBeNull();

        var comp = registry.GetComponent("orders-lock");
        comp.ShouldNotBeNull();
        comp.Provider.ShouldBe("sqlserver");
    }
}
