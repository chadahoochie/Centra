using Centra.Hosting.Discovery;
using Centra.PubSub;
using Centra.PubSub.Routing;
using Centra.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

public sealed class PubSubServiceCollectionSerializerExtensionsTests
{
    [Fact]
    public void AddCentraPubSubSerializer_Type_Registers_In_DI_And_Registry()
    {
        var services = new ServiceCollection();
        services.AddCentraPubSubSerializer<CustomTestSerializer>();
        var sp = services.BuildServiceProvider();

        var typedSerializer = sp.GetService<CustomTestSerializer>();
        typedSerializer.ShouldNotBeNull();

        var pubSubSerializer = sp.GetService<IPubSubSerializer>();
        pubSubSerializer.ShouldNotBeNull();
        pubSubSerializer.ShouldBeSameAs(typedSerializer);

        var registry = sp.GetRequiredService<IPubSubSerializerRegistry>();
        registry.TryGetSerializer("application/x-custom", out var found).ShouldBeTrue();
        found.ShouldBeSameAs(typedSerializer);
    }

    [Fact]
    public void AddCentraPubSubSerializer_Instance_Registers_In_DI_And_Registry()
    {
        var services = new ServiceCollection();
        var custom = new CustomTestSerializer();
        services.AddCentraPubSubSerializer(custom);
        var sp = services.BuildServiceProvider();

        var pubSubSerializer = sp.GetService<IPubSubSerializer>();
        pubSubSerializer.ShouldNotBeNull();
        pubSubSerializer.ShouldBeSameAs(custom);

        var registry = sp.GetRequiredService<IPubSubSerializerRegistry>();
        registry.TryGetSerializer("application/x-custom", out var found).ShouldBeTrue();
        found.ShouldBeSameAs(custom);
    }

    [Fact]
    public void AddCentraPubSubSerializer_Instance_With_Explicit_ContentType_Overrides_Default()
    {
        var services = new ServiceCollection();
        var custom = new CustomTestSerializer();
        services.AddCentraPubSubSerializer(custom, "application/x-override");
        var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IPubSubSerializerRegistry>();
        registry.TryGetSerializer("application/x-override", out var found).ShouldBeTrue();
        found.ShouldBeSameAs(custom);
    }

    [Fact]
    public void AddCentraEventHandler_With_Explicit_Serializer_Instance_Populates_Registration()
    {
        var services = new ServiceCollection();
        var custom = new CustomTestSerializer();

        services.AddCentraEventHandler<CustomTestEventHandler, CustomTestEvent>(
            pubSubName: "test-bus",
            topic: "test-topic",
            serializer: custom);

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(CentraTopicRegistration))
            .Select(sd => (CentraTopicRegistration)sd.ImplementationInstance!)
            .ToList();

        registrations.Count.ShouldBe(1);
        registrations[0].Serializer.ShouldBeSameAs(custom);
        registrations[0].Topic.ShouldBe("test-topic");
    }

    [Fact]
    public void AddCentraEventHandler_With_Explicit_SerializerType_And_ContentType_Populates_Registration()
    {
        var services = new ServiceCollection();

        services.AddCentraEventHandler<CustomTestEventHandler, CustomTestEvent>(
            pubSubName: "test-bus",
            topic: "test-topic",
            serializerType: typeof(CustomTestSerializer),
            contentType: "application/x-custom");

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(CentraTopicRegistration))
            .Select(sd => (CentraTopicRegistration)sd.ImplementationInstance!)
            .ToList();

        registrations.Count.ShouldBe(1);
        registrations[0].SerializerType.ShouldBe(typeof(CustomTestSerializer));
        registrations[0].ContentType.ShouldBe("application/x-custom");
    }

    [Fact]
    public void AttributeScanner_Discovers_SerializerType_And_ContentType()
    {
        var services = new ServiceCollection();
        CentraAttributeScanner.RegisterIfEventHandler(services, typeof(CustomAnnotatedEventHandler));

        var registrations = services
            .Where(sd => sd.ServiceType == typeof(CentraTopicRegistration))
            .Select(sd => (CentraTopicRegistration)sd.ImplementationInstance!)
            .ToList();

        registrations.Count.ShouldBe(1);
        registrations[0].Topic.ShouldBe("annotated-topic");
        registrations[0].SerializerType.ShouldBe(typeof(CustomTestSerializer));
        registrations[0].ContentType.ShouldBe("application/x-custom");
    }
}
