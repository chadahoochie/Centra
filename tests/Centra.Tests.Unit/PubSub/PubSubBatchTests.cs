using Centra.Drivers;
using Centra.Events;
using Centra.Providers.InMemory.PubSub;
using Centra.PubSub;
using Centra.Registry;
using Centra.Tests.Unit.Common;
using Centra.Tests.Unit.Events;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

[Collection("CentraDiagnostics")]
public sealed class PubSubBatchTests
{
    private readonly ComponentRegistry _registry = new();
    private readonly IPubSubDriver _driver = Substitute.For<IPubSubDriver>();

    public PubSubBatchTests()
    {
        _registry.RegisterPubSubDriver("event-bus", _driver);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Publish_Batch_Events_Successfully(
        string topic,
        string orderId1,
        string orderId2)
    {
        // Arrange
        var items = new[]
        {
            new TestOrderCreatedEvent(orderId1, "prod-1", 10),
            new TestOrderCreatedEvent(orderId2, "prod-2", 20)
        };
        var client = new CentraPubSubClient(_registry, "orders-service", "event-bus");

        // Act
        await client.PublishBatchAsync(topic, items);

        // Assert
        await _driver.Received(1).PublishBatchAsync(
            "event-bus",
            topic,
            Arg.Is<IReadOnlyList<PubSubMessage>>(msgs =>
                msgs.Count == 2 &&
                msgs[0].Payload.Length > 0 &&
                msgs[1].Payload.Length > 0 &&
                msgs[0].Metadata != null &&
                msgs[0].Metadata![CloudEventConstants.TypeHeader] == "orders.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Not_Call_Driver_When_Batch_Is_Empty()
    {
        // Arrange
        var client = new CentraPubSubClient(_registry, "orders-service", "event-bus");

        // Act
        await client.PublishBatchAsync("some-topic", Array.Empty<TestOrderCreatedEvent>());

        // Assert
        await _driver.DidNotReceiveWithAnyArgs().PublishBatchAsync(
            default!, default!, default!, default);
    }

    [Fact]
    public async Task Should_Dispatch_Batch_To_Subscribers_With_InMemoryDriver()
    {
        // Arrange
        var inMemoryDriver = new InMemoryPubSubDriver();
        var registry = new ComponentRegistry();
        registry.RegisterPubSubDriver("in-mem-bus", inMemoryDriver);

        var client = new CentraPubSubClient(registry, "test-app", "in-mem-bus");
        var received = new List<string>();

        await inMemoryDriver.SubscribeAsync(
            "in-mem-bus",
            "batch.topic",
            (payload, headers, ct) =>
            {
                var unpacked = CloudEventUnpacker.Unpack<TestOrderCreatedEvent>(payload, headers);
                lock (received)
                {
                    received.Add(unpacked.Data!.OrderId);
                }
                return ValueTask.FromResult(EventHandlingResult.Success);
            });

        var events = new[]
        {
            new TestOrderCreatedEvent("ord-1", "p1", 1),
            new TestOrderCreatedEvent("ord-2", "p2", 2),
            new TestOrderCreatedEvent("ord-3", "p3", 3)
        };

        // Act
        await client.PublishBatchAsync("batch.topic", events);

        // Assert
        received.Count.ShouldBe(3);
        received.ShouldContain("ord-1");
        received.ShouldContain("ord-2");
        received.ShouldContain("ord-3");
    }

    [Fact]
    public async Task Should_Propagate_TraceParent_Header_To_Batch_Messages()
    {
        // Arrange
        using var listener = new TestActivityListener("Centra");
        IReadOnlyList<PubSubMessage>? capturedMessages = null;

        await _driver.PublishBatchAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Do<IReadOnlyList<PubSubMessage>>(msgs => capturedMessages = msgs),
            Arg.Any<CancellationToken>());

        var client = new CentraPubSubClient(_registry, "orders-service", "event-bus");
        var events = new[]
        {
            new TestOrderCreatedEvent("ord-100", "p1", 10),
            new TestOrderCreatedEvent("ord-101", "p2", 20)
        };

        // Act
        await client.PublishBatchAsync("trace.topic", events);

        // Assert
        capturedMessages.ShouldNotBeNull();
        capturedMessages.Count.ShouldBe(2);
        capturedMessages[0].Metadata.ShouldNotBeNull();
        capturedMessages[0].Metadata!.ShouldContainKey("traceparent");
        capturedMessages[0].Metadata!["traceparent"].ShouldStartWith("00-");
        capturedMessages[1].Metadata.ShouldNotBeNull();
        capturedMessages[1].Metadata!.ShouldContainKey("traceparent");
        capturedMessages[1].Metadata!["traceparent"].ShouldStartWith("00-");
    }
}
