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
}
