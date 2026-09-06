using Centra.Components;
using Centra.Drivers;
using Centra.Providers.RabbitMQ.Extensions;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.Extensions;

public sealed class RabbitMQHostingExtensionsTests
{
    [Fact]
    public void AddCentraRabbitMQ_Should_Register_Driver_And_Component()
    {
        var services = new ServiceCollection();
        var factory = Substitute.For<IConnectionFactory>();
        services.AddSingleton(factory);

        services.AddCentraRabbitMQ(options =>
        {
            options.DefaultPubSubName = "rmq-pubsub";
            options.ExchangeName = "custom.exchange";
        });

        var provider = services.BuildServiceProvider();

        provider.GetService<RabbitMQPubSubDriver>().ShouldNotBeNull();
        provider.GetService<IPubSubDriver>().ShouldNotBeNull();

        var initializer = provider.GetRequiredService<IComponentInitializer>();
        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        registry.GetPubSubDriver("rmq-pubsub").ShouldNotBeNull();

        var pubsubDef = registry.GetComponent("rmq-pubsub");
        pubsubDef.ShouldNotBeNull();
        pubsubDef.Type.ShouldBe(ComponentType.PubSub);
        pubsubDef.Provider.ShouldBe("rabbitmq");
    }
}
