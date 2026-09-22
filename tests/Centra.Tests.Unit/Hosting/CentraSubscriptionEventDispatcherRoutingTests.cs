using System.Text;
using Centra.Events;
using Centra.PubSub.HostedServices;
using Centra.PubSub.Routing;
using Centra.PubSub;
using Centra.PubSub.Routing.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraSubscriptionEventDispatcherRoutingTests
{
    private sealed record DummyEvent(string Id);

    private sealed class FastOrderHandler : IEventHandler<DummyEvent>
    {
        public bool Handled { get; set; }
        public Task<EventHandlingResult> HandleAsync(DummyEvent @event, EventContext context, CancellationToken ct)
        {
            Handled = true;
            return Task.FromResult(EventHandlingResult.Success);
        }
    }

    private sealed class SlowOrderHandler : IEventHandler<DummyEvent>
    {
        public bool Handled { get; set; }
        public Task<EventHandlingResult> HandleAsync(DummyEvent @event, EventContext context, CancellationToken ct)
        {
            Handled = true;
            return Task.FromResult(EventHandlingResult.Success);
        }
    }

    [Fact]
    public async Task DispatchEventAsync_SelectsMatchingRouteAndInvokesHandler()
    {
        var services = new ServiceCollection();
        var fastHandler = new FastOrderHandler();
        var slowHandler = new SlowOrderHandler();
        services.AddSingleton(fastHandler);
        services.AddSingleton(slowHandler);
        var sp = services.BuildServiceProvider();

        var evaluator = new RuleFilterEvaluator();

        var regFast = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyEvent),
            typeof(FastOrderHandler),
            deadLetterTopic: null,
            invoker: (h, p, hdrs, ct) => ((FastOrderHandler)h).HandleAsync(new DummyEvent("1"), default, ct),
            ruleFilter: "data.speed == 'fast'",
            priority: 10,
            compiledFilter: evaluator.Compile("data.speed == 'fast'"));

        var regSlow = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyEvent),
            typeof(SlowOrderHandler),
            deadLetterTopic: null,
            invoker: (h, p, hdrs, ct) => ((SlowOrderHandler)h).HandleAsync(new DummyEvent("2"), default, ct),
            ruleFilter: "data.speed == 'slow'",
            priority: 5,
            compiledFilter: evaluator.Compile("data.speed == 'slow'"));

        var router = new CentraTopicRouter("bus", "orders", [regFast, regSlow]);
        var dispatcher = new CentraSubscriptionEventDispatcher(sp, NullLogger.Instance);

        var fastPayload = Encoding.UTF8.GetBytes("""{"speed":"fast"}""");
        var headers = new Dictionary<string, string>();

        var result = await dispatcher.DispatchEventAsync(router, fastPayload, headers, CancellationToken.None);

        result.ShouldBe(EventHandlingResult.Success);
        fastHandler.Handled.ShouldBeTrue();
        slowHandler.Handled.ShouldBeFalse();
    }

    [Fact]
    public async Task DispatchEventAsync_UnroutedEventWithoutDefault_ReturnsSuccess()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var evaluator = new RuleFilterEvaluator();
        var regFast = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyEvent),
            typeof(FastOrderHandler),
            deadLetterTopic: null,
            invoker: (h, p, hdrs, ct) => Task.FromResult(EventHandlingResult.Success),
            ruleFilter: "data.speed == 'fast'",
            priority: 10,
            compiledFilter: evaluator.Compile("data.speed == 'fast'"));

        var router = new CentraTopicRouter("bus", "orders", [regFast]);
        var dispatcher = new CentraSubscriptionEventDispatcher(sp, NullLogger.Instance);

        var unknownPayload = Encoding.UTF8.GetBytes("""{"speed":"unknown"}""");
        var result = await dispatcher.DispatchEventAsync(router, unknownPayload, new Dictionary<string, string>(), CancellationToken.None);

        result.ShouldBe(EventHandlingResult.Success);
    }
}
