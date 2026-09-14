using System.Text;
using Centra.Events;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Centra.PubSub.Routing.Rules;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraTopicRouterTests
{
    private sealed record TestOrderEvent(string Id, int Amount);
    private sealed class HighValueHandler : IEventHandler<TestOrderEvent>
    {
        public Task<EventHandlingResult> HandleAsync(TestOrderEvent @event, EventContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(EventHandlingResult.Success);
    }
    private sealed class StandardHandler : IEventHandler<TestOrderEvent>
    {
        public Task<EventHandlingResult> HandleAsync(TestOrderEvent @event, EventContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(EventHandlingResult.Success);
    }
    private sealed class DefaultHandler : IEventHandler<TestOrderEvent>
    {
        public Task<EventHandlingResult> HandleAsync(TestOrderEvent @event, EventContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(EventHandlingResult.Success);
    }

    [Fact]
    public void SelectRoute_MatchesHighestPriorityRouteFirst()
    {
        var evaluator = new RuleFilterEvaluator();

        var highValueReg = new CentraTopicRegistration(
            "pubsub",
            "orders",
            typeof(TestOrderEvent),
            typeof(HighValueHandler),
            deadLetterTopic: null,
            invoker: null,
            ruleFilter: "data.amount > 1000",
            priority: 20,
            compiledFilter: evaluator.Compile("data.amount > 1000"));

        var standardReg = new CentraTopicRegistration(
            "pubsub",
            "orders",
            typeof(TestOrderEvent),
            typeof(StandardHandler),
            deadLetterTopic: null,
            invoker: null,
            ruleFilter: "data.amount <= 1000",
            priority: 10,
            compiledFilter: evaluator.Compile("data.amount <= 1000"));

        var defaultReg = new CentraTopicRegistration(
            "pubsub",
            "orders",
            typeof(TestOrderEvent),
            typeof(DefaultHandler));

        var router = new CentraTopicRouter("pubsub", "orders", [defaultReg, standardReg, highValueReg]);

        router.RequiresDataPayload.ShouldBeTrue();
        router.DefaultRoute.ShouldBe(defaultReg);

        var highPayload = Encoding.UTF8.GetBytes("""{"id":"1","amount":5000}""");
        var stdPayload = Encoding.UTF8.GetBytes("""{"id":"2","amount":500}""");
        var badPayload = Encoding.UTF8.GetBytes("""{"id":"3"}""");

        var context = new EventContext("1", "orders", "pubsub", "pos", "TestOrderEvent", DateTimeOffset.UtcNow, null, null, null, new Dictionary<string, string>());

        // 5000 -> HighValueHandler
        var selected1 = router.SelectRoute(highPayload, context.Headers, in context);
        selected1.ShouldBe(highValueReg);

        // 500 -> StandardHandler
        var selected2 = router.SelectRoute(stdPayload, context.Headers, in context);
        selected2.ShouldBe(standardReg);

        // badPayload -> DefaultHandler
        var selected3 = router.SelectRoute(badPayload, context.Headers, in context);
        selected3.ShouldBe(defaultReg);
    }

    [Fact]
    public void SelectRoute_NoMatchingRule_NoDefault_ReturnsNull()
    {
        var evaluator = new RuleFilterEvaluator();

        var v1Reg = new CentraTopicRegistration(
            "pubsub",
            "orders",
            typeof(TestOrderEvent),
            typeof(StandardHandler),
            deadLetterTopic: null,
            invoker: null,
            ruleFilter: "event.type == 'v1'",
            priority: 10,
            compiledFilter: evaluator.Compile("event.type == 'v1'"));

        var router = new CentraTopicRouter("pubsub", "orders", [v1Reg]);

        var context = new EventContext("1", "orders", "pubsub", "src", "v2", DateTimeOffset.UtcNow, null, null, null, new Dictionary<string, string>());
        var selected = router.SelectRoute(ReadOnlyMemory<byte>.Empty, context.Headers, in context);

        selected.ShouldBeNull();
    }

    [Fact]
    public void SelectRoute_ProgrammaticPredicate_Matches()
    {
        var predicateReg = new CentraTopicRegistration(
            "pubsub",
            "orders",
            typeof(TestOrderEvent),
            typeof(HighValueHandler),
            deadLetterTopic: null,
            invoker: null,
            ruleFilter: null,
            priority: 10,
            compiledFilter: null,
            predicate: (ctx, bytes) => ctx.Headers.ContainsKey("x-special"));

        var defaultReg = new CentraTopicRegistration(
            "pubsub",
            "orders",
            typeof(TestOrderEvent),
            typeof(DefaultHandler));

        var router = new CentraTopicRouter("pubsub", "orders", [defaultReg, predicateReg]);

        var specialHeaders = new Dictionary<string, string> { ["x-special"] = "true" };
        var contextSpecial = new EventContext("1", "orders", "pubsub", "src", "type", DateTimeOffset.UtcNow, null, null, null, specialHeaders);

        var normalHeaders = new Dictionary<string, string>();
        var contextNormal = new EventContext("2", "orders", "pubsub", "src", "type", DateTimeOffset.UtcNow, null, null, null, normalHeaders);

        router.SelectRoute(ReadOnlyMemory<byte>.Empty, specialHeaders, in contextSpecial).ShouldBe(predicateReg);
        router.SelectRoute(ReadOnlyMemory<byte>.Empty, normalHeaders, in contextNormal).ShouldBe(defaultReg);
    }
}
