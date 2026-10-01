using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.PubSub;
using Centra.Providers.Flotilla.Tcp.Client;
using Centra.Providers.Flotilla.Tcp.Extensions;
using Centra.Providers.Flotilla.Tcp.Options;
using Centra.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaTcpHostingExtensionsTests
{
    [Fact]
    public void AddCentraFlotillaTcp_RegistersAllExpectedServices()
    {
        var services = new ServiceCollection();
        services.AddCentraFlotillaTcp(options =>
        {
            options.DefaultPubSubName = "consensus-tcp";
            options.ClusterNodes = ["10.0.0.1:9001", "10.0.0.2:9001"];
            options.ClientTimeoutMs = 1500;
        });

        using var sp = services.BuildServiceProvider();

        var client = sp.GetService<IFlotillaClient>();
        client.ShouldNotBeNull();
        client.ShouldBeOfType<FlotillaTcpClient>();

        var concreteDriver = sp.GetService<FlotillaPubSubDriver>();
        concreteDriver.ShouldNotBeNull();

        var pubSubDriver = sp.GetService<IPubSubDriver>();
        pubSubDriver.ShouldNotBeNull();
        pubSubDriver.ShouldBeSameAs(concreteDriver);

        var drain = sp.GetService<IPubSubShutdownDrain>();
        drain.ShouldNotBeNull();
        drain.ShouldBeSameAs(concreteDriver);

        var options = sp.GetRequiredService<IOptions<FlotillaTcpOptions>>().Value;
        options.DefaultPubSubName.ShouldBe("consensus-tcp");
        options.ClusterNodes.Length.ShouldBe(2);
        options.ClientTimeoutMs.ShouldBe(1500);

        var initializers = sp.GetServices<IComponentInitializer>().ToList();
        initializers.ShouldContain(i => i is FlotillaTcpComponentInitializer);

        var hostedServices = sp.GetServices<IHostedService>().ToList();
        hostedServices.ShouldContain(h => h is FlotillaSubscriptionWorker);
    }

    [Fact]
    public void FlotillaTcpComponentInitializer_InitializesComponentRegistry()
    {
        var services = new ServiceCollection();
        services.AddCentraFlotillaTcp(options =>
        {
            options.DefaultPubSubName = "flotilla-tcp-main";
        });

        using var sp = services.BuildServiceProvider();

        var initializer = sp.GetServices<IComponentInitializer>()
            .OfType<FlotillaTcpComponentInitializer>()
            .Single();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var driver = registry.GetPubSubDriver("flotilla-tcp-main");
        driver.ShouldNotBeNull();
        driver.ShouldBeOfType<FlotillaPubSubDriver>();

        var component = registry.GetComponent("flotilla-tcp-main");
        component.ShouldNotBeNull();
        component.Provider.ShouldBe("flotilla-tcp");
    }

    [Fact]
    public void AddCentraFlotillaTcpPubSub_RegistersNamedInstance()
    {
        var services = new ServiceCollection();
        services.AddCentraFlotillaTcpPubSub("orders-channel");

        using var sp = services.BuildServiceProvider();

        var initializers = sp.GetServices<IComponentInitializer>().ToList();
        initializers.ShouldContain(i => i is FlotillaTcpDelegateComponentInitializer);

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
        component.Provider.ShouldBe("flotilla-tcp");
    }
}
