using Centra.Events;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraTopicRegistrationTests
{
    private sealed record TestTopicEvent(string Id);

    private sealed class StubTopicEventHandler : IEventHandler<TestTopicEvent>
    {
        public bool Handled { get; private set; }
        public TestTopicEvent? ReceivedEvent { get; private set; }

        public Task<EventHandlingResult> HandleAsync(TestTopicEvent @event, EventContext context, CancellationToken cancellationToken = default)
        {
            Handled = true;
            ReceivedEvent = @event;
            return Task.FromResult(EventHandlingResult.Success);
        }
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var customArgs = new Dictionary<string, object?> { ["x-custom"] = "value" };
        var reg = new CentraTopicRegistration(
            "bus",
            "topic1",
            typeof(TestTopicEvent),
            typeof(StubTopicEventHandler),
            deadLetterTopic: "dlq",
            consumerMode: ConsumerMode.SingleActiveConsumer,
            prefetchCount: 10,
            maxConcurrentCalls: 5,
            messageTimeToLive: TimeSpan.FromSeconds(30),
            autoDelete: true,
            customArguments: customArgs);

        reg.PubSubName.ShouldBe("bus");
        reg.Topic.ShouldBe("topic1");
        reg.EventType.ShouldBe(typeof(TestTopicEvent));
        reg.HandlerType.ShouldBe(typeof(StubTopicEventHandler));
        reg.DeadLetterTopic.ShouldBe("dlq");
        reg.ConsumerMode.ShouldBe(ConsumerMode.SingleActiveConsumer);
        reg.PrefetchCount.ShouldBe(10);
        reg.MaxConcurrentCalls.ShouldBe(5);
        reg.MessageTimeToLive.ShouldBe(TimeSpan.FromSeconds(30));
        reg.AutoDelete.ShouldBeTrue();
        reg.CustomArguments.ShouldBe(customArgs);
        reg.Invoker.ShouldNotBeNull();
    }

    [Fact]
    public async Task DefaultInvoker_UnpackedDataNull_ReturnsDrop()
    {
        var reg = new CentraTopicRegistration(
            "bus",
            "topic1",
            typeof(TestTopicEvent),
            typeof(StubTopicEventHandler));

        var handler = new StubTopicEventHandler();

        // Empty bytes with no CloudEvents headers unpacks to null data
        var result = await reg.Invoker(handler, ReadOnlyMemory<byte>.Empty, new Dictionary<string, string>(), CancellationToken.None);

        result.ShouldBe(EventHandlingResult.Drop);
        handler.Handled.ShouldBeFalse();
    }

    [Fact]
    public async Task DefaultInvoker_ValidCloudEvent_InvokesHandler()
    {
        var reg = new CentraTopicRegistration(
            "bus",
            "topic1",
            typeof(TestTopicEvent),
            typeof(StubTopicEventHandler));

        var handler = new StubTopicEventHandler();
        var packed = CloudEventPacker.Pack(new TestTopicEvent("evt-123"), "test-source");

        var result = await reg.Invoker(handler, packed.Payload, packed.Headers, CancellationToken.None);

        result.ShouldBe(EventHandlingResult.Success);
        handler.Handled.ShouldBeTrue();
        handler.ReceivedEvent.ShouldNotBeNull();
        handler.ReceivedEvent.Id.ShouldBe("evt-123");
    }
}
