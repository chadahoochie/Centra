using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Centra.Tests.Unit.Tenancy;

public sealed class EphemeralBrokerTopicOffloadStrategySafetyTests
{
    [Fact]
    public async Task CleanupIdleResourcesAsync_DoesNotUnsubscribe_When_ActiveConsumerTurns_Exist()
    {
        var publisher = new TestPubSubPublisher();
        var subscriber = new TestPubSubSubscriber();
        var fakeTime = new FakeTimeProvider();
        var turnTracker = new EphemeralTopicTurnTracker();
        var options = new TenantOffloadOptions
        {
            OffloadTopicPattern = "{topic}.offload.{tenantId}",
            LaneIdleTimeout = TimeSpan.FromSeconds(1)
        };

        var strategy = new EphemeralBrokerTopicOffloadStrategy(
            publisher,
            subscriber,
            options,
            fakeTime,
            queueInspector: null,
            turnTracker: turnTracker);

        var workItem = new TenantOffloadWorkItem(
            TenantId: "tenant-turn-test",
            PubSubName: "pubsub",
            Topic: "orders.events",
            Payload: new byte[] { 1 },
            Headers: new Dictionary<string, string>(),
            HandlerInvoker: _ => ValueTask.FromResult(EventHandlingResult.Success),
            CreatedAt: fakeTime.GetUtcNow());

        await strategy.ExecuteOffloadAsync(workItem, CancellationToken.None);
        Assert.Single(strategy.ActiveOffloadTopics);

        // Simulate an active in-flight message processing turn
        var topicKey = ("pubsub", "orders.events.offload.tenant-turn-test");
        turnTracker.Enter(topicKey);

        // Advance past idle timeout
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        await strategy.CleanupIdleResourcesAsync(CancellationToken.None);

        // Should NOT unsubscribe because active turns > 0
        Assert.Empty(subscriber.Unsubscriptions);
        Assert.Single(strategy.ActiveOffloadTopics);

        // Exit turn and retry
        turnTracker.Exit(topicKey);
        await strategy.CleanupIdleResourcesAsync(CancellationToken.None);

        Assert.Single(subscriber.Unsubscriptions);
        Assert.Empty(strategy.ActiveOffloadTopics);
    }

    [Fact]
    public async Task CleanupIdleResourcesAsync_DoesNotUnsubscribe_When_QueueDepth_IsGreaterThanZero()
    {
        var publisher = new TestPubSubPublisher();
        var subscriber = new TestPubSubSubscriber();
        var fakeTime = new FakeTimeProvider();
        var inspector = Substitute.For<IPubSubQueueInspector>();
        var options = new TenantOffloadOptions
        {
            OffloadTopicPattern = "{topic}.offload.{tenantId}",
            LaneIdleTimeout = TimeSpan.FromSeconds(1)
        };

        var strategy = new EphemeralBrokerTopicOffloadStrategy(
            publisher,
            subscriber,
            options,
            fakeTime,
            queueInspector: inspector);

        var workItem = new TenantOffloadWorkItem(
            TenantId: "tenant-depth-test",
            PubSubName: "pubsub",
            Topic: "orders.events",
            Payload: new byte[] { 1 },
            Headers: new Dictionary<string, string>(),
            HandlerInvoker: _ => ValueTask.FromResult(EventHandlingResult.Success),
            CreatedAt: fakeTime.GetUtcNow());

        await strategy.ExecuteOffloadAsync(workItem, CancellationToken.None);

        // Queue inspector reports 5 messages still waiting in the broker queue
        inspector.GetQueueStatsAsync("pubsub", "orders.events.offload.tenant-depth-test", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<PubSubQueueStats?>(new PubSubQueueStats(MessageCount: 5, ConsumerCount: 1)));

        // Advance past idle timeout
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        await strategy.CleanupIdleResourcesAsync(CancellationToken.None);

        // Queue still has backlog; reaper must refrain from deleting
        Assert.Empty(subscriber.Unsubscriptions);
        Assert.Single(strategy.ActiveOffloadTopics);

        // Now queue drains to 0
        inspector.GetQueueStatsAsync("pubsub", "orders.events.offload.tenant-depth-test", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<PubSubQueueStats?>(new PubSubQueueStats(MessageCount: 0, ConsumerCount: 1)));

        await strategy.CleanupIdleResourcesAsync(CancellationToken.None);

        // Queue safely unsubscribed
        Assert.Single(subscriber.Unsubscriptions);
        Assert.Empty(strategy.ActiveOffloadTopics);
    }
}
