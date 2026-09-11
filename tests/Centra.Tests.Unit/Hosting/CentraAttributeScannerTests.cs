using Centra.Events;
using Centra.Hosting.Extensions;
using Centra.Hosting.Routing;
using Centra.Invocation;
using Centra.PubSub;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraAttributeScannerTests
{
    [Fact]
    public void AddCentra_Should_AutoRegister_TopicAttribute_Handler_With_No_Explicit_Call()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(configure: null, typeof(CentraAttributeScannerTests).Assembly);

        var serviceProvider = services.BuildServiceProvider();

        var registrations = serviceProvider.GetServices<CentraTopicRegistration>()
            .Where(r => r.HandlerType == typeof(ScannedOnlyEventHandler))
            .ToList();

        registrations.Count.ShouldBe(1);
        registrations[0].PubSubName.ShouldBe("pubsub");
        registrations[0].Topic.ShouldBe("scanned.only.topic");
    }

    [Fact]
    public void Explicit_AddCentraEventHandler_Call_Should_Override_Scanned_Registration_Without_Duplicate()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(configure: null, typeof(CentraAttributeScannerTests).Assembly);

        // Mirrors the real-world pattern: AddCentra() scans and finds [Topic("pubsub", ...)],
        // then an explicit call further down Program.cs overrides the pubsub name.
        services.AddCentraEventHandler<ScannedOnlyEventHandler, ScannedEvent>(pubSubName: "orders-pubsub");

        var serviceProvider = services.BuildServiceProvider();

        var registrations = serviceProvider.GetServices<CentraTopicRegistration>()
            .Where(r => r.HandlerType == typeof(ScannedOnlyEventHandler))
            .ToList();

        registrations.Count.ShouldBe(1);
        registrations[0].PubSubName.ShouldBe("orders-pubsub");
        registrations[0].Topic.ShouldBe("scanned.only.topic");
    }

    [Fact]
    public void AddCentra_Should_AutoRegister_ServiceClient_With_No_Explicit_Call()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(configure: null, typeof(CentraAttributeScannerTests).Assembly);

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetService<IScannedOnlyServiceClient>();

        client.ShouldNotBeNull();
    }

    [Fact]
    public void AddCentra_Should_AutoRegister_TopicAttribute_With_Extended_Options()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(configure: null, typeof(CentraAttributeScannerTests).Assembly);

        var serviceProvider = services.BuildServiceProvider();

        var reg = serviceProvider.GetServices<CentraTopicRegistration>()
            .FirstOrDefault(r => r.HandlerType == typeof(ConfiguredOptionsEventHandler));

        reg.ShouldNotBeNull();
        reg.PubSubName.ShouldBe("custom-bus");
        reg.Topic.ShouldBe("options.topic");
        reg.PrefetchCount.ShouldBe(25);
        reg.MaxConcurrentCalls.ShouldBe(4);
        reg.AutoDelete.ShouldBeTrue();
        reg.MessageTimeToLive.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Explicit_AddCentraEventHandler_With_All_Options_Should_Populate_Registration()
    {
        var services = new ServiceCollection();
        var customArgs = new Dictionary<string, object?> { ["x-max-length"] = 500 };

        services.AddCentraEventHandler<ScannedOnlyEventHandler, ScannedEvent>(
            pubSubName: "custom-bus",
            topic: "explicit.topic",
            deadLetterTopic: "dlq.topic",
            consumerMode: ConsumerMode.SingleActiveConsumer,
            prefetchCount: 15,
            maxConcurrentCalls: 3,
            messageTimeToLive: TimeSpan.FromMinutes(5),
            autoDelete: true,
            customArguments: customArgs);

        var sp = services.BuildServiceProvider();
        var reg = sp.GetRequiredService<CentraTopicRegistration>();

        reg.PubSubName.ShouldBe("custom-bus");
        reg.Topic.ShouldBe("explicit.topic");
        reg.DeadLetterTopic.ShouldBe("dlq.topic");
        reg.ConsumerMode.ShouldBe(ConsumerMode.SingleActiveConsumer);
        reg.PrefetchCount.ShouldBe(15);
        reg.MaxConcurrentCalls.ShouldBe(3);
        reg.MessageTimeToLive.ShouldBe(TimeSpan.FromMinutes(5));
        reg.AutoDelete.ShouldBeTrue();
        reg.CustomArguments.ShouldBe(customArgs);
    }

    public sealed record ScannedEvent(string Value);

    [ServiceClient("scanned-service")]
    public interface IScannedOnlyServiceClient
    {
        [ServiceMethod("test", "GET")]
        Task<string> GetTestAsync();
    }

    [Topic("pubsub", "scanned.only.topic")]
    public sealed class ScannedOnlyEventHandler : IEventHandler<ScannedEvent>
    {
        public Task<EventHandlingResult> HandleAsync(ScannedEvent @event, EventContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(EventHandlingResult.Success);
    }

    [Topic("custom-bus", "options.topic", PrefetchCount = 25, MaxConcurrentCalls = 4, AutoDelete = true, MessageTtlSeconds = 60)]
    public sealed class ConfiguredOptionsEventHandler : IEventHandler<ScannedEvent>
    {
        public Task<EventHandlingResult> HandleAsync(ScannedEvent @event, EventContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(EventHandlingResult.Success);
    }
}
