using Centra.Events;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraTopicSubscriptionOptionsResolverTests
{
    private sealed record DummyEvent;
    private sealed class DummyHandler : IEventHandler<DummyEvent>
    {
        public Task<EventHandlingResult> HandleAsync(DummyEvent @event, EventContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(EventHandlingResult.Success);
    }

    [Fact]
    public void Resolve_EmptyRegistrations_ReturnsDefaults()
    {
        var (options, dlq) = CentraTopicSubscriptionOptionsResolver.Resolve([]);
        options.ConsumerMode.ShouldBe(ConsumerMode.CompetingConsumer);
        options.PrefetchCount.ShouldBeNull();
        options.MaxConcurrentCalls.ShouldBeNull();
        options.MessageTimeToLive.ShouldBeNull();
        options.AutoDelete.ShouldBeFalse();
        dlq.ShouldBeNull();
    }

    [Fact]
    public void Resolve_MultipleRegistrations_MergesOptionsCorrectly()
    {
        var reg1 = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyEvent),
            typeof(DummyHandler),
            deadLetterTopic: "dlq-primary",
            invoker: null,
            ruleFilter: "event.type == 'v1'",
            priority: 10,
            compiledFilter: null,
            predicate: null,
            consumerMode: ConsumerMode.CompetingConsumer,
            prefetchCount: 10,
            maxConcurrentCalls: 4,
            messageTimeToLive: TimeSpan.FromMinutes(5),
            autoDelete: true,
            customArguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });

        var reg2 = new CentraTopicRegistration(
            "bus",
            "orders",
            typeof(DummyEvent),
            typeof(DummyHandler),
            deadLetterTopic: "dlq-secondary",
            invoker: null,
            ruleFilter: "event.type == 'v2'",
            priority: 20,
            compiledFilter: null,
            predicate: null,
            consumerMode: ConsumerMode.SingleActiveConsumer,
            prefetchCount: 50,
            maxConcurrentCalls: 8,
            messageTimeToLive: TimeSpan.FromMinutes(2),
            autoDelete: false,
            customArguments: new Dictionary<string, object?> { ["x-max-length"] = 1000 });

        var (options, dlq) = CentraTopicSubscriptionOptionsResolver.Resolve([reg1, reg2]);

        options.ConsumerMode.ShouldBe(ConsumerMode.SingleActiveConsumer);
        options.PrefetchCount.ShouldBe(50);
        options.MaxConcurrentCalls.ShouldBe(8);
        options.MessageTimeToLive.ShouldBe(TimeSpan.FromMinutes(2));
        options.AutoDelete.ShouldBeFalse();
        dlq.ShouldBe("dlq-primary");
        options.CustomArguments.ShouldNotBeNull();
        options.CustomArguments["x-queue-type"].ShouldBe("quorum");
        options.CustomArguments["x-max-length"].ShouldBe(1000);
    }
}
