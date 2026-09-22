using Centra.Events;
using Centra.Hosting.Extensions;
using Centra.PubSub.Routing;
using Centra.PubSub;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraAttributeScannerMultiTopicTests
{
    public sealed record MultiRouteEvent(string Id);

    [Topic("bus-a", "topic-1", RuleFilter = "event.type == 'v1'", Priority = 20)]
    [Topic("bus-b", "topic-2", RuleFilter = "event.type == 'v2'", Priority = 10)]
    public sealed class MultiTopicEventHandler : IEventHandler<MultiRouteEvent>
    {
        public Task<EventHandlingResult> HandleAsync(MultiRouteEvent @event, EventContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(EventHandlingResult.Success);
    }

    [Fact]
    public void ScanAndRegister_MultipleTopicAttributes_RegistersAllRoutesWithRules()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(configure: null, typeof(CentraAttributeScannerMultiTopicTests).Assembly);

        var serviceProvider = services.BuildServiceProvider();

        var registrations = serviceProvider.GetServices<CentraTopicRegistration>()
            .Where(r => r.HandlerType == typeof(MultiTopicEventHandler))
            .ToList();

        registrations.Count.ShouldBe(2);

        var route1 = registrations.FirstOrDefault(r => r.PubSubName == "bus-a");
        route1.ShouldNotBeNull();
        route1.Topic.ShouldBe("topic-1");
        route1.RuleFilter.ShouldBe("event.type == 'v1'");
        route1.Priority.ShouldBe(20);
        route1.CompiledFilter.ShouldNotBeNull();

        var route2 = registrations.FirstOrDefault(r => r.PubSubName == "bus-b");
        route2.ShouldNotBeNull();
        route2.Topic.ShouldBe("topic-2");
        route2.RuleFilter.ShouldBe("event.type == 'v2'");
        route2.Priority.ShouldBe(10);
        route2.CompiledFilter.ShouldNotBeNull();
    }
}
