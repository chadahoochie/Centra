using Centra.Drivers;
using Centra.Hosting.Outbox;
using Centra.PubSub.Outbox;
using Centra.State;
using Centra.Tests.Unit.Common;
using Centra.Tests.Unit.Events;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

public sealed class OutboxTests
{
    private readonly IPubSubDriver _driver = Substitute.For<IPubSubDriver>();
    private readonly IStateStore _stateStore = Substitute.For<IStateStore>();

    [Theory, AutoNSubstituteData]
    public async Task Should_Enqueue_Message_To_Outbox_And_Contain_CloudEvent_Headers(
        string topic,
        string orderId,
        string productId,
        int quantity)
    {
        var outboxStore = new InMemoryOutboxStore();
        var publisher = new CentraOutboxPublisher(outboxStore, "event-bus", "orders-app");
        var evt = new TestOrderCreatedEvent(orderId, productId, quantity);

        await publisher.EnqueueAsync(topic, evt);

        outboxStore.PendingCount.ShouldBe(1);
        var pending = await outboxStore.FetchPendingAsync(10);
        pending.Count.ShouldBe(1);
        pending[0].Topic.ShouldBe(topic);
        pending[0].PubSubName.ShouldBe("event-bus");
        pending[0].Headers.ContainsKey("ce-id").ShouldBeTrue();
        pending[0].Headers["ce-type"].ShouldBe("orders.created");
        pending[0].Headers["ce-source"].ShouldBe("orders-app");
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Process_Pending_Outbox_Messages_And_Mark_Published(
        string topic,
        string orderId,
        string productId,
        int quantity)
    {
        var outboxStore = new InMemoryOutboxStore();
        var publisher = new CentraOutboxPublisher(outboxStore, "event-bus", "orders-app");
        var evt = new TestOrderCreatedEvent(orderId, productId, quantity);

        await publisher.EnqueueAsync(topic, evt);

        var processor = new OutboxProcessor(outboxStore, _driver);
        var publishedCount = await processor.ProcessPendingAsync();

        publishedCount.ShouldBe(1);
        outboxStore.PendingCount.ShouldBe(0);

        await _driver.Received(1).PublishAsync(
            "event-bus",
            topic,
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Mark_Failed_When_Publisher_Throws(
        string topic,
        string orderId,
        string productId,
        int quantity)
    {
        var outboxStore = new InMemoryOutboxStore();
        var publisher = new CentraOutboxPublisher(outboxStore, "event-bus", "orders-app");
        var evt = new TestOrderCreatedEvent(orderId, productId, quantity);

        await publisher.EnqueueAsync(topic, evt);

        _driver.PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("Broker connection failed")));

        var processor = new OutboxProcessor(outboxStore, _driver);
        var publishedCount = await processor.ProcessPendingAsync();

        publishedCount.ShouldBe(0);
        // Message remains in pending for retry
        outboxStore.PendingCount.ShouldBe(1);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Enqueue_And_Fetch_With_StateStoreOutboxStore(
        string messageId,
        string topic)
    {
        var storedIndex = new StateEntry<List<string>>("centra:outbox:pending", new List<string> { messageId }, "etag-1");
        var messageRecord = new OutboxMessageRecord(messageId, "pubsub", topic, [1, 2, 3], new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        var storedMsg = new StateEntry<OutboxMessageRecord>($"centra:outbox:msg:{messageId}", messageRecord, "etag-2");

        _stateStore.GetAsync<List<string>>("statestore", "centra:outbox:pending", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(storedIndex);

        _stateStore.GetAsync<OutboxMessageRecord>("statestore", $"centra:outbox:msg:{messageId}", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(storedMsg);

        var store = new StateStoreOutboxStore(_stateStore, "statestore");
        var pending = await store.FetchPendingAsync(10);

        pending.Count.ShouldBe(1);
        pending[0].Id.ShouldBe(messageId);
        pending[0].Topic.ShouldBe(topic);
    }
}
