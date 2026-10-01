using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Grpc.Client;
using Centra.Providers.Flotilla.Grpc.Extensions;
using Centra.Providers.Flotilla.Grpc.Options;
using Centra.Providers.Flotilla.PubSub;
using Centra.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaGrpcHostingExtensionsTests
{
    [Fact]
    public void AddCentraFlotillaGrpc_RegistersAllExpectedServices()
    {
        var services = new ServiceCollection();
        services.AddCentraFlotillaGrpc(options =>
        {
            options.DefaultPubSubName = "consensus-grpc";
            options.ClusterNodes = ["http://10.0.0.1:9001", "http://10.0.0.2:9001"];
            options.ClientTimeoutMs = 1200;
        });

        using var sp = services.BuildServiceProvider();

        var client = sp.GetService<IFlotillaClient>();
        client.ShouldNotBeNull();
        client.ShouldBeOfType<FlotillaGrpcClient>();

        var concreteDriver = sp.GetService<FlotillaPubSubDriver>();
        concreteDriver.ShouldNotBeNull();

        var pubSubDriver = sp.GetService<IPubSubDriver>();
        pubSubDriver.ShouldNotBeNull();
        pubSubDriver.ShouldBeSameAs(concreteDriver);

        var drain = sp.GetService<IPubSubShutdownDrain>();
        drain.ShouldNotBeNull();
        drain.ShouldBeSameAs(concreteDriver);

        var options = sp.GetRequiredService<IOptions<FlotillaGrpcOptions>>().Value;
        options.DefaultPubSubName.ShouldBe("consensus-grpc");
        options.ClusterNodes.Length.ShouldBe(2);
        options.ClientTimeoutMs.ShouldBe(1200);

        var initializers = sp.GetServices<IComponentInitializer>().ToList();
        initializers.ShouldContain(i => i is FlotillaGrpcComponentInitializer);

        var hostedServices = sp.GetServices<IHostedService>().ToList();
        hostedServices.ShouldContain(h => h is FlotillaSubscriptionWorker);
    }

    [Fact]
    public void FlotillaGrpcComponentInitializer_InitializesComponentRegistry()
    {
        var services = new ServiceCollection();
        services.AddCentraFlotillaGrpc(options =>
        {
            options.DefaultPubSubName = "flotilla-grpc-main";
        });

        using var sp = services.BuildServiceProvider();

        var initializer = sp.GetServices<IComponentInitializer>()
            .OfType<FlotillaGrpcComponentInitializer>()
            .Single();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var driver = registry.GetPubSubDriver("flotilla-grpc-main");
        driver.ShouldNotBeNull();
        driver.ShouldBeOfType<FlotillaPubSubDriver>();

        var component = registry.GetComponent("flotilla-grpc-main");
        component.ShouldNotBeNull();
        component.Provider.ShouldBe("flotilla-grpc");
    }

    [Fact]
    public void AddCentraFlotillaGrpcPubSub_RegistersNamedInstance()
    {
        var services = new ServiceCollection();
        services.AddCentraFlotillaGrpcPubSub("orders-channel");

        using var sp = services.BuildServiceProvider();

        var initializers = sp.GetServices<IComponentInitializer>().ToList();
        initializers.ShouldContain(i => i is FlotillaGrpcDelegateComponentInitializer);

        var registry = new ComponentRegistry();
        foreach (var init in initializers)
        {
            init.Initialize(registry);
        }

        var driver = registry.GetPubSubDriver("orders-channel");
        driver.ShouldNotBeNull();

        var component = registry.GetComponent("orders-channel");
        component.ShouldNotBeNull();
        component.Name.ShouldBe("orders-channel");
        component.Provider.ShouldBe("flotilla-grpc");
    }
}
