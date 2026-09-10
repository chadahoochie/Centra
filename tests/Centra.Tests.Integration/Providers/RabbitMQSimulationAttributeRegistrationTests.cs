using Centra.Hosting.Extensions;
using Centra.Hosting.Routing;
using Centra.Sample.RabbitSimulation.Consumer.Handlers;
using Centra.Sample.RabbitSimulation.Contracts.Clients;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.Providers;

public sealed class RabbitMQSimulationAttributeRegistrationTests
{
    [Fact]
    public void Should_AutoRegister_Consumer_And_ServiceClient_Via_Attributes_Only()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(options =>
        {
            options.AppId = "rabbit-consumer";
            options.DefaultPubSub = "pubsub";
        }, typeof(OrderSubmittedEventHandler).Assembly, typeof(IOrderApiClient).Assembly);

        var sp = services.BuildServiceProvider();

        // 1. Verify IOrderApiClient was registered via [ServiceClient("rabbit-api")]
        var client = sp.GetService<IOrderApiClient>();
        client.ShouldNotBeNull();

        // 2. Verify OrderSubmittedEventHandler was registered via [Topic("pubsub", "orders.new")]
        var topicRegistrations = sp.GetServices<CentraTopicRegistration>()
            .Where(r => r.HandlerType == typeof(OrderSubmittedEventHandler))
            .ToList();

        topicRegistrations.Count.ShouldBe(1);
        topicRegistrations[0].PubSubName.ShouldBe("pubsub");
        topicRegistrations[0].Topic.ShouldBe("orders.new");

        // 3. Verify handler resolves cleanly with all auto-registered dependencies
        var handler = sp.GetService<OrderSubmittedEventHandler>();
        handler.ShouldNotBeNull();
    }
}
