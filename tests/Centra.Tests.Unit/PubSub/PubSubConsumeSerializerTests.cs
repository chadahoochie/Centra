using Centra.Events;
using Centra.PubSub;
using Centra.PubSub.HostedServices;
using Centra.PubSub.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

public sealed class PubSubConsumeSerializerTests
{
    [Fact]
    public async Task ExecuteAsync_With_Explicit_Serializer_Invokes_Handler()
    {
        var custom = new CustomTestSerializer();
        var handler = new CustomTestEventHandler();
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "test-bus",
            "test-topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler),
            serializerAwareInvoker: (h, p, hdrs, s, ct) =>
            {
                var evt = s!.Deserialize<CustomTestEvent>(p)!;
                return ((CustomTestEventHandler)h).HandleAsync(evt, default, ct);
            },
            serializer: custom);

        var payload = custom.Serialize(new CustomTestEvent("Explicit Serializer", 100));
        var headers = new Dictionary<string, string>();

        var result = await CentraSubscriptionPipelineExecutor.ExecuteAsync(
            sp,
            reg,
            payload,
            headers,
            null,
            CancellationToken.None);

        result.ShouldBe(EventHandlingResult.Success);
        handler.ReceivedEvent.ShouldNotBeNull();
        handler.ReceivedEvent.Message.ShouldBe("Explicit Serializer");
        handler.ReceivedEvent.Value.ShouldBe(100);
    }

    [Fact]
    public async Task ExecuteAsync_Resolves_Serializer_From_Header_ContentType_Via_Registry()
    {
        var custom = new CustomTestSerializer();
        var handler = new CustomTestEventHandler();
        var registry = new CentraPubSubSerializerRegistry();
        registry.Register("application/x-custom", custom);

        var services = new ServiceCollection();
        services.AddSingleton<IPubSubSerializerRegistry>(registry);
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "test-bus",
            "test-topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler),
            serializerAwareInvoker: (h, p, hdrs, s, ct) =>
            {
                var evt = s!.Deserialize<CustomTestEvent>(p)!;
                return ((CustomTestEventHandler)h).HandleAsync(evt, default, ct);
            });

        var payload = custom.Serialize(new CustomTestEvent("Registry Dispatch", 200));
        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.DataContentTypeHeader] = "application/x-custom"
        };

        var result = await CentraSubscriptionPipelineExecutor.ExecuteAsync(
            sp,
            reg,
            payload,
            headers,
            null,
            CancellationToken.None);

        result.ShouldBe(EventHandlingResult.Success);
        handler.ReceivedEvent.ShouldNotBeNull();
        handler.ReceivedEvent.Message.ShouldBe("Registry Dispatch");
        handler.ReceivedEvent.Value.ShouldBe(200);
    }

    [Fact]
    public async Task ExecuteAsync_Resolves_Serializer_From_Registration_SerializerType()
    {
        var handler = new CustomTestEventHandler();
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        services.AddSingleton<CustomTestSerializer>();
        var sp = services.BuildServiceProvider();

        var reg = new CentraTopicRegistration(
            "test-bus",
            "test-topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler),
            serializerAwareInvoker: (h, p, hdrs, s, ct) =>
            {
                var evt = s!.Deserialize<CustomTestEvent>(p)!;
                return ((CustomTestEventHandler)h).HandleAsync(evt, default, ct);
            },
            serializerType: typeof(CustomTestSerializer));

        var custom = new CustomTestSerializer();
        var payload = custom.Serialize(new CustomTestEvent("Type Dispatch", 300));
        var headers = new Dictionary<string, string>();

        var result = await CentraSubscriptionPipelineExecutor.ExecuteAsync(
            sp,
            reg,
            payload,
            headers,
            null,
            CancellationToken.None);

        result.ShouldBe(EventHandlingResult.Success);
        handler.ReceivedEvent.ShouldNotBeNull();
        handler.ReceivedEvent.Message.ShouldBe("Type Dispatch");
        handler.ReceivedEvent.Value.ShouldBe(300);
    }

    [Fact]
    public async Task CentraTopicRegistration_InvokeAsync_Falls_Back_To_Legacy_Invoker()
    {
        var handler = new CustomTestEventHandler();
        var invokedLegacy = false;

        var reg = new CentraTopicRegistration(
            "test-bus",
            "test-topic",
            typeof(CustomTestEvent),
            typeof(CustomTestEventHandler),
            invoker: (h, p, hdrs, ct) =>
            {
                invokedLegacy = true;
                return Task.FromResult(EventHandlingResult.Success);
            });

        var custom = new CustomTestSerializer();
        var result = await reg.InvokeAsync(
            handler,
            ReadOnlyMemory<byte>.Empty,
            new Dictionary<string, string>(),
            custom,
            CancellationToken.None);

        result.ShouldBe(EventHandlingResult.Success);
        invokedLegacy.ShouldBeTrue();
    }
}
