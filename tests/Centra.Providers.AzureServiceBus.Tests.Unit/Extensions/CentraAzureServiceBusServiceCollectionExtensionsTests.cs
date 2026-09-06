using Centra.Components;
using Centra.Drivers;
using Centra.Providers.AzureServiceBus.Extensions;
using Centra.Providers.AzureServiceBus.Options;
using Centra.Providers.AzureServiceBus.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.AzureServiceBus.Tests.Unit.Extensions;

public sealed class CentraAzureServiceBusServiceCollectionExtensionsTests
{
    private const string ValidConnStr = "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=fakekey";

    [Fact]
    public async Task AddCentraAzureServiceBus_Should_Register_Driver_And_Initializer()
    {
        var services = new ServiceCollection();
        services.AddCentraAzureServiceBus(opts =>
        {
            opts.ConnectionString = ValidConnStr;
            opts.DefaultPubSubName = "servicebus-events";
            opts.TopicPrefix = "test.";
        });

        await using var sp = services.BuildServiceProvider();

        var options = sp.GetService<IOptions<AzureServiceBusProviderOptions>>();
        options.ShouldNotBeNull();
        options.Value.DefaultPubSubName.ShouldBe("servicebus-events");
        options.Value.TopicPrefix.ShouldBe("test.");

        var driver = sp.GetService<AzureServiceBusPubSubDriver>();
        driver.ShouldNotBeNull();

        var driverInterface = sp.GetService<IPubSubDriver>();
        driverInterface.ShouldBeSameAs(driver);

        var initializer = sp.GetService<IComponentInitializer>();
        initializer.ShouldNotBeNull();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var registeredDriver = registry.GetPubSubDriver("servicebus-events");
        registeredDriver.ShouldBeSameAs(driver);

        var component = registry.GetComponent("servicebus-events");
        component.ShouldNotBeNull();
        component.Type.ShouldBe(ComponentType.PubSub);
        component.Provider.ShouldBe("azureservicebus");
    }

    [Fact]
    public async Task AddCentraAzureServiceBusPubSub_Should_Register_Granular_PubSub()
    {
        var services = new ServiceCollection();
        services.AddCentraAzureServiceBusPubSub("audit-pubsub", opts =>
        {
            opts.ConnectionString = ValidConnStr;
        });

        await using var sp = services.BuildServiceProvider();

        var initializer = sp.GetService<IComponentInitializer>();
        initializer.ShouldNotBeNull();

        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        var registered = registry.GetPubSubDriver("audit-pubsub");
        registered.ShouldNotBeNull();

        var component = registry.GetComponent("audit-pubsub");
        component.ShouldNotBeNull();
        component.Provider.ShouldBe("azureservicebus");
    }
}
