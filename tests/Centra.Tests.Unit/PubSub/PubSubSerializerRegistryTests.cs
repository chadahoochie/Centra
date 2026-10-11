using Centra.Events;
using Centra.PubSub;
using Centra.PubSub.Routing;
using Centra.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

public sealed class PubSubSerializerRegistryTests
{
    [Fact]
    public void Registry_Defaults_To_ApplicationJson_Serializer()
    {
        var registry = new CentraPubSubSerializerRegistry();

        var serializer = registry.GetSerializer("application/json");

        serializer.ShouldNotBeNull();
        serializer.ShouldBeSameAs(JsonCentraSerializer.Default);
        registry.TryGetSerializer("application/json", out var found).ShouldBeTrue();
        found.ShouldBeSameAs(JsonCentraSerializer.Default);
    }

    [Fact]
    public void Registry_Register_And_Retrieve_Custom_Serializer()
    {
        var registry = new CentraPubSubSerializerRegistry();
        var custom = new CustomTestSerializer();

        registry.Register("application/x-custom", custom);

        registry.GetSerializer("application/x-custom").ShouldBeSameAs(custom);
        registry.TryGetSerializer("application/x-custom", out var found).ShouldBeTrue();
        found.ShouldBeSameAs(custom);
    }

    [Fact]
    public void Registry_Register_Throws_On_Null_Or_Whitespace_ContentType()
    {
        var registry = new CentraPubSubSerializerRegistry();
        var custom = new CustomTestSerializer();

        Should.Throw<ArgumentException>(() => registry.Register("", custom));
        Should.Throw<ArgumentException>(() => registry.Register("   ", custom));
    }

    [Fact]
    public void Registry_Register_Throws_On_Null_Serializer()
    {
        var registry = new CentraPubSubSerializerRegistry();

        Should.Throw<ArgumentNullException>(() => registry.Register("application/x-custom", null!));
    }

    [Fact]
    public void Registry_Case_Insensitive_Lookup()
    {
        var registry = new CentraPubSubSerializerRegistry();
        var custom = new CustomTestSerializer();

        registry.Register("application/x-custom", custom);

        registry.GetSerializer("APPLICATION/X-CUSTOM").ShouldBeSameAs(custom);
        registry.TryGetSerializer("Application/X-Custom", out var found).ShouldBeTrue();
        found.ShouldBeSameAs(custom);
    }

    [Fact]
    public void Registry_Strips_Media_Type_Parameters_Like_Charset()
    {
        var registry = new CentraPubSubSerializerRegistry();
        var custom = new CustomTestSerializer();

        registry.Register("application/x-custom", custom);

        registry.GetSerializer("application/x-custom; charset=utf-8").ShouldBeSameAs(custom);
        registry.TryGetSerializer("application/x-custom; charset=utf-8", out var found).ShouldBeTrue();
        found.ShouldBeSameAs(custom);
    }

    [Fact]
    public void Registry_Returns_Null_Or_False_For_Unknown_Or_Empty_ContentType()
    {
        var registry = new CentraPubSubSerializerRegistry();

        registry.GetSerializer("application/unknown").ShouldBeNull();
        registry.TryGetSerializer("application/unknown", out var notFound).ShouldBeFalse();
        notFound.ShouldBeNull();

        registry.GetSerializer("").ShouldBeNull();
        registry.TryGetSerializer("", out var emptyFound).ShouldBeFalse();
        emptyFound.ShouldBeNull();
    }

    [Fact]
    public void Resolver_Prefers_Registration_Serializer_Instance()
    {
        var custom = new CustomTestSerializer();
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler),
            serializer: custom);

        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.DataContentTypeHeader] = "application/json"
        };

        var resolved = CentraPubSubSerializerResolver.Resolve(sp, reg, headers);

        resolved.ShouldBeSameAs(custom);
    }

    [Fact]
    public void Resolver_Prefers_Registration_SerializerType_From_DI()
    {
        var custom = new CustomTestSerializer();
        var services = new ServiceCollection();
        services.AddSingleton(custom);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler),
            serializerType: typeof(CustomTestSerializer));

        var headers = new Dictionary<string, string>();

        var resolved = CentraPubSubSerializerResolver.Resolve(sp, reg, headers);

        resolved.ShouldBeSameAs(custom);
    }

    [Fact]
    public void Resolver_Creates_Registration_SerializerType_When_Not_In_DI()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler),
            serializerType: typeof(CustomTestSerializer));

        var headers = new Dictionary<string, string>();

        var resolved = CentraPubSubSerializerResolver.Resolve(sp, reg, headers);

        resolved.ShouldNotBeNull();
        resolved.ShouldBeOfType<CustomTestSerializer>();
    }

    [Fact]
    public void Resolver_Uses_Header_ContentType_From_Registry()
    {
        var custom = new CustomTestSerializer();
        var registry = new CentraPubSubSerializerRegistry();
        registry.Register("application/x-custom", custom);

        var services = new ServiceCollection();
        services.AddSingleton<IPubSubSerializerRegistry>(registry);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler));

        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.DataContentTypeHeader] = "application/x-custom; charset=utf-8"
        };

        var resolved = CentraPubSubSerializerResolver.Resolve(sp, reg, headers);

        resolved.ShouldBeSameAs(custom);
    }

    [Fact]
    public void Resolver_Uses_Registration_ContentType_From_Registry()
    {
        var custom = new CustomTestSerializer();
        var registry = new CentraPubSubSerializerRegistry();
        registry.Register("application/x-custom", custom);

        var services = new ServiceCollection();
        services.AddSingleton<IPubSubSerializerRegistry>(registry);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler),
            contentType: "application/x-custom");

        var headers = new Dictionary<string, string>();

        var resolved = CentraPubSubSerializerResolver.Resolve(sp, reg, headers);

        resolved.ShouldBeSameAs(custom);
    }

    [Fact]
    public void Resolver_Falls_Back_To_DI_IPubSubSerializer()
    {
        var custom = new CustomTestSerializer();
        var services = new ServiceCollection();
        services.AddSingleton<IPubSubSerializer>(custom);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler));

        var headers = new Dictionary<string, string>();

        var resolved = CentraPubSubSerializerResolver.Resolve(sp, reg, headers);

        resolved.ShouldBeSameAs(custom);
    }

    [Fact]
    public void Resolver_Falls_Back_To_DI_ICentraSerializer()
    {
        var custom = new CustomTestSerializer();
        var services = new ServiceCollection();
        services.AddSingleton<ICentraSerializer>(custom);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler));

        var headers = new Dictionary<string, string>();

        var resolved = CentraPubSubSerializerResolver.Resolve(sp, reg, headers);

        resolved.ShouldBeSameAs(custom);
    }

    [Fact]
    public void Resolver_Falls_Back_To_JsonCentraSerializer_Default()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "bus",
            "topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler));

        var headers = new Dictionary<string, string>();

        var resolved = CentraPubSubSerializerResolver.Resolve(sp, reg, headers);

        resolved.ShouldBeSameAs(JsonCentraSerializer.Default);
    }
}
