using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.PubSub;
using Centra.Providers.Flotilla.Udp.Client;
using Centra.Providers.Flotilla.Udp.Extensions;
using Centra.Providers.Flotilla.Udp.Options;
using Centra.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaUdpHostingExtensionsTests
{
    [Fact]
    public void AddCentraFlotillaUdp_RegistersAllExpectedServices()
    {
        var services = new ServiceCollection();
        services.AddCentraFlotillaUdp(options =>
        {
            options.DefaultPubSubName = "consensus-udp";
            options.ClusterNodes = ["10.0.0.1:9001", "10.0.0.2:9001"];
            options.ClientTimeoutMs = 75;
        });

        using var sp = services.BuildServiceProvider();

        var client = sp.GetService<IFlotillaClient>();
        client.ShouldNotBeNull();
        client.ShouldBeOfType<FlotillaUdpClient>();

        var concreteDriver = sp.GetService<FlotillaPubSubDriver>();
        concreteDriver.ShouldNotBeNull();

        var pubSubDriver = sp.GetService<IPubSubDriver>();
        pubSubDriver.ShouldNotBeNull();
        pubSubDriver.ShouldBeSameAs(concreteDriver);

        var drain = sp.GetService<IPubSubShutdownDrain>();
        drain.ShouldNotBeNull();
        drain.ShouldBeSameAs(concreteDriver);

        var options = sp.GetRequiredService<IOptions<FlotillaUdpOptions>>().Value;
        options.DefaultPubSubName.ShouldBe("consensus-udp");
        options.ClusterNodes.Length.ShouldBe(2);
        options.ClientTimeoutMs.ShouldBe(75);

        var initializers = sp.GetServices<IComponentInitializer>().ToList();
        initializers.ShouldContain(i => i is FlotillaUdpComponentInitializer);

        var hostedServices = sp.GetServices<IHostedService>().ToList();
        hostedServices.ShouldContain(h => h is FlotillaSubscriptionWorker);
    }

    [Fact]
    public void FlotillaUdpComponentInitializer_InitializesComponentRegistry()
    {
        var services = new ServiceCollection();
        services.AddCentraFlotillaUdp(options =>
        {
            options.DefaultPubSubName = "flotilla-udp-main";
        });

        using var sp = services.BuildServiceProvider();

        var initializer = sp.GetServices<IComponentInitializer>()
            .OfType<FlotillaUdpComponentInitializer>()
            .Single();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var driver = registry.GetPubSubDriver("flotilla-udp-main");
        driver.ShouldNotBeNull();
        driver.ShouldBeOfType<FlotillaPubSubDriver>();

        var component = registry.GetComponent("flotilla-udp-main");
        component.ShouldNotBeNull();
        component.Provider.ShouldBe("flotilla-udp");
    }

    [Fact]
    public void AddCentraFlotillaUdpPubSub_RegistersNamedInstance()
    {
        var services = new ServiceCollection();
        services.AddCentraFlotillaUdpPubSub("orders-channel");

        using var sp = services.BuildServiceProvider();

        var initializers = sp.GetServices<IComponentInitializer>().ToList();
        initializers.ShouldContain(i => i is FlotillaUdpDelegateComponentInitializer);

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
        component.Provider.ShouldBe("flotilla-udp");
    }
}
