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
}
