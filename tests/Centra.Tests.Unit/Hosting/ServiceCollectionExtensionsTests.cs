using Centra.Hosting.Extensions;
using Centra.Hosting.Options;
using Centra.Invocation;
using Centra.Locks;
using Centra.Providers.InMemory.Extensions;
using Centra.PubSub;
using Centra.State;
using Centra.Tests.Unit.Common;
using Centra.Tests.Unit.Invocation;
using Centra.Tests.Unit.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Should_Register_Core_Centra_Services_In_DI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(options =>
        {
            options.AppId = "test-host-app";
            options.DefaultStateStore = "orders-store";
        });
        services.AddCentraInMemory();

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var options = serviceProvider.GetRequiredService<IOptions<CentraOptions>>().Value;
        options.AppId.ShouldBe("test-host-app");
        options.DefaultStateStore.ShouldBe("orders-store");

        var stateStore = serviceProvider.GetService<IStateStore>();
        stateStore.ShouldNotBeNull();

        var genericStore = serviceProvider.GetService<IStateStore<TestStateOrder>>();
        genericStore.ShouldNotBeNull();

        var pubSubClient = serviceProvider.GetService<IPubSubClient>();
        pubSubClient.ShouldNotBeNull();

        var lockProvider = serviceProvider.GetService<IDistributedLockProvider>();
        lockProvider.ShouldNotBeNull();

        var invoker = serviceProvider.GetService<IServiceInvoker>();
        invoker.ShouldNotBeNull();
    }

    [Fact]
    public void Should_Register_Typed_Service_Client_Proxy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra();
        services.AddCentraInMemory();
        services.AddCentraServiceClient<ITestInventoryService>();

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var client = serviceProvider.GetService<ITestInventoryService>();

        // Assert
        client.ShouldNotBeNull();
    }
}
