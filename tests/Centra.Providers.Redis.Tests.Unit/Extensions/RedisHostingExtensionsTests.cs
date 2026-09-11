using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Redis.Extensions;
using Centra.Providers.Redis.Locks;
using Centra.Providers.Redis.PubSub;
using Centra.Providers.Redis.State;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.Extensions;

public sealed class RedisHostingExtensionsTests
{
    [Fact]
    public void AddCentraRedis_Should_Register_All_Redis_Components_And_Drivers()
    {
        var services = new ServiceCollection();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(multiplexer);

        services.AddCentraRedis(options =>
        {
            options.DefaultStateStoreName = "custom-state";
            options.DefaultPubSubName = "custom-pubsub";
            options.DefaultLockStoreName = "custom-locks";
        });

        var provider = services.BuildServiceProvider();

        provider.GetService<RedisStateStoreDriver>().ShouldNotBeNull();
        provider.GetService<RedisPubSubDriver>().ShouldNotBeNull();
        provider.GetService<RedisDistributedLockDriver>().ShouldNotBeNull();
        provider.GetService<IStateStoreDriver>().ShouldNotBeNull();
        provider.GetService<IPubSubDriver>().ShouldNotBeNull();
        provider.GetService<IDistributedLockDriver>().ShouldNotBeNull();

        var initializer = provider.GetRequiredService<IComponentInitializer>();
        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        registry.GetStateStoreDriver("custom-state").ShouldNotBeNull();
        registry.GetPubSubDriver("custom-pubsub").ShouldNotBeNull();
        registry.GetLockDriver("custom-locks").ShouldNotBeNull();

        registry.GetComponent("custom-state").ShouldNotBeNull();
        registry.GetComponent("custom-state")!.Type.ShouldBe(ComponentType.StateStore);
        registry.GetComponent("custom-state")!.Provider.ShouldBe("redis");
    }

    [Fact]
    public void AddCentraRedisStateStore_Should_Register_StateStore_Component_And_Driver()
    {
        var services = new ServiceCollection();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(multiplexer);

        services.AddCentraRedisStateStore("my-redis-state", options =>
        {
            options.ConnectionString = "localhost:6379";
        });

        var provider = services.BuildServiceProvider();
        var driver = provider.GetService<RedisStateStoreDriver>();
        driver.ShouldNotBeNull();

        var initializer = provider.GetRequiredService<IComponentInitializer>();
        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        registry.GetStateStoreDriver("my-redis-state").ShouldNotBeNull();
        registry.GetComponent("my-redis-state")!.Type.ShouldBe(ComponentType.StateStore);
    }

    [Fact]
    public void AddCentraRedisPubSub_Should_Register_PubSub_Component_And_Driver()
    {
        var services = new ServiceCollection();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(multiplexer);

        services.AddCentraRedisPubSub("my-redis-pubsub", options =>
        {
            options.ConnectionString = "localhost:6379";
        });

        var provider = services.BuildServiceProvider();
        var driver = provider.GetService<RedisPubSubDriver>();
        driver.ShouldNotBeNull();

        var initializer = provider.GetRequiredService<IComponentInitializer>();
        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        registry.GetPubSubDriver("my-redis-pubsub").ShouldNotBeNull();
        registry.GetComponent("my-redis-pubsub")!.Type.ShouldBe(ComponentType.PubSub);
    }

    [Fact]
    public void AddCentraRedisLocks_Should_Register_Locks_Component_And_Driver()
    {
        var services = new ServiceCollection();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(multiplexer);

        services.AddCentraRedisLocks("my-redis-locks", options =>
        {
            options.ConnectionString = "localhost:6379";
        });

        var provider = services.BuildServiceProvider();
        var driver = provider.GetService<RedisDistributedLockDriver>();
        driver.ShouldNotBeNull();

        var initializer = provider.GetRequiredService<IComponentInitializer>();
        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        registry.GetLockDriver("my-redis-locks").ShouldNotBeNull();
        registry.GetComponent("my-redis-locks")!.Type.ShouldBe(ComponentType.DistributedLock);
    }

    [Fact]
    public void Extensions_Should_Support_Parameterless_Options()
    {
        var services = new ServiceCollection();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(multiplexer);

        services.AddCentraRedis();
        services.AddCentraRedisStateStore("state-default");
        services.AddCentraRedisPubSub("pubsub-default");
        services.AddCentraRedisLocks("locks-default");

        var provider = services.BuildServiceProvider();
        provider.GetService<RedisStateStoreDriver>().ShouldNotBeNull();
        provider.GetService<RedisPubSubDriver>().ShouldNotBeNull();
        provider.GetService<RedisDistributedLockDriver>().ShouldNotBeNull();
    }
}
