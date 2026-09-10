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

    [Fact]
    public void AddCentraRabbitMQ_WithoutConfigure_Should_Register_Defaults()
    {
        var services = new ServiceCollection();
        services.AddCentraRabbitMQ();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConnectionFactory>();
        factory.ShouldNotBeNull();
    }

    [Fact]
    public void AddCentraRabbitMQ_WithConnectionString_Should_ConfigureFactory()
    {
        var services = new ServiceCollection();
        services.AddCentraRabbitMQ(opts => opts.ConnectionString = "amqp://guest:guest@localhost:5672/");

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConnectionFactory>() as ConnectionFactory;
        factory.ShouldNotBeNull();
        factory.HostName.ShouldBe("localhost");
        factory.Port.ShouldBe(5672);
        factory.UserName.ShouldBe("guest");
    }

    [Fact]
    public void AddCentraRabbitMQ_WithUri_Should_ConfigureFactory()
    {
        var services = new ServiceCollection();
        var uri = new Uri("amqp://admin:secret@broker.local:5672/vhost");
        services.AddCentraRabbitMQ(opts => opts.Uri = uri);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConnectionFactory>() as ConnectionFactory;
        factory.ShouldNotBeNull();
        factory.HostName.ShouldBe("broker.local");
        factory.Port.ShouldBe(5672);
        factory.UserName.ShouldBe("admin");
        factory.VirtualHost.ShouldBe("vhost");
    }

    [Fact]
    public void AddCentraRabbitMQPubSub_Should_Register_Named_PubSub()
    {
        var services = new ServiceCollection();
        var factory = Substitute.For<IConnectionFactory>();
        services.AddSingleton(factory);

        services.AddCentraRabbitMQPubSub("orders-pubsub", opts =>
        {
            opts.HostName = "127.0.0.1";
            opts.Port = 5672;
        });

        var provider = services.BuildServiceProvider();
        provider.GetService<RabbitMQPubSubDriver>().ShouldNotBeNull();

        var initializer = provider.GetRequiredService<IComponentInitializer>();
        var registry = new ComponentRegistry();
        initializer.Initialize(registry);

        registry.GetPubSubDriver("orders-pubsub").ShouldNotBeNull();
        var comp = registry.GetComponent("orders-pubsub");
        comp.ShouldNotBeNull();
        comp.Name.ShouldBe("orders-pubsub");
        comp.Type.ShouldBe(ComponentType.PubSub);
        comp.Provider.ShouldBe("rabbitmq");
    }

    [Fact]
    public void AddCentraRabbitMQPubSub_WithoutConfigure_Should_Register_Defaults()
    {
        var services = new ServiceCollection();
        services.AddCentraRabbitMQPubSub("default-pubsub");

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConnectionFactory>();
        factory.ShouldNotBeNull();
    }
}
