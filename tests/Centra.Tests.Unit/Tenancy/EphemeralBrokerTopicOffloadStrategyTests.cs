using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Xunit;

namespace Centra.Tests.Unit.Tenancy;

public sealed class EphemeralBrokerTopicOffloadStrategyTests
{
    [Fact]
    public async Task ExecuteOffloadAsync_PublishesToOffloadTopicAndAcksMainTopic()
    {
        var publisher = new TestPubSubPublisher();
        var options = new TenantOffloadOptions
        {
            OffloadTopicPattern = "{topic}.offload.{tenantId}"
        };

        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, subscriber: null, options);

        var payload = new byte[] { 1, 2, 3 };
        var headers = new Dictionary<string, string> { ["custom-header"] = "val" };

        var workItem = new TenantOffloadWorkItem(
            TenantId: "tenant-ABC",
            PubSubName: "pubsub",
            Topic: "orders.created",
            Payload: payload,
            Headers: headers,
            HandlerInvoker: _ => ValueTask.FromResult(EventHandlingResult.Success),
            CreatedAt: DateTimeOffset.UtcNow);

        var result = await strategy.ExecuteOffloadAsync(workItem, CancellationToken.None);

        Assert.Equal(EventHandlingResult.Success, result);
        Assert.Single(publisher.PublishedMessages);

        var published = publisher.PublishedMessages[0];
        Assert.Equal("pubsub", published.PubSubName);
        Assert.Equal("orders.created.offload.tenant-ABC", published.Topic);
        Assert.Equal(payload, published.Payload.ToArray());
        Assert.Equal("true", published.Metadata["ce-offloaded"]);
        Assert.Equal("val", published.Metadata["custom-header"]);
    }

    [Fact]
    public void ResolveTopic_ReplacesTokensCorrectly()
    {
        var publisher = new TestPubSubPublisher();
        var options = new TenantOffloadOptions
        {
            OffloadTopicPattern = "quarantine.{topic}.{tenantId}"
        };

        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, subscriber: null, options);
        var resolved = strategy.ResolveTopic("payments", "tenant-99");

        Assert.Equal("quarantine.payments.tenant-99", resolved);
    }

    [Fact]
    public async Task ExecuteOffloadAsync_WithSubscriber_SubscribesToOffloadTopicAndInvokesDynamicInvoker()
    {
        var publisher = new TestPubSubPublisher();
        var subscriber = new TestPubSubSubscriber();
        var options = new TenantOffloadOptions
        {
            OffloadTopicPattern = "{topic}.offload.{tenantId}"
        };

        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, subscriber, options);

        var payload = new byte[] { 10, 20, 30 };
        var headers = new Dictionary<string, string> { ["x-tenant"] = "tenant-XYZ" };
        var dynamicInvokerCalled = false;

        var workItem = new TenantOffloadWorkItem(
            TenantId: "tenant-XYZ",
            PubSubName: "pubsub",
            Topic: "orders.events",
            Payload: payload,
            Headers: headers,
            HandlerInvoker: _ => ValueTask.FromResult(EventHandlingResult.Success),
            CreatedAt: DateTimeOffset.UtcNow,
            CompletionSource: null,
            DynamicInvoker: (p, h, ct) =>
            {
                dynamicInvokerCalled = true;
                Assert.Equal(payload, p.ToArray());
                return ValueTask.FromResult(EventHandlingResult.Success);
            });

        var result = await strategy.ExecuteOffloadAsync(workItem, CancellationToken.None);

        Assert.Equal(EventHandlingResult.Success, result);
        Assert.Single(subscriber.Subscriptions);
        Assert.Equal("pubsub", subscriber.Subscriptions[0].PubSubName);
        Assert.Equal("orders.events.offload.tenant-XYZ", subscriber.Subscriptions[0].Topic);
        Assert.NotNull(subscriber.Subscriptions[0].Options);
        Assert.True(subscriber.Subscriptions[0].Options!.AutoDelete);

        // Invoke the handler that the subscriber registered
        var handler = subscriber.Subscriptions[0].Handler;
        var handlerResult = await handler(payload, headers, CancellationToken.None);

        Assert.Equal(EventHandlingResult.Success, handlerResult);
        Assert.True(dynamicInvokerCalled);
        Assert.Single(strategy.ActiveOffloadTopics);
    }

    [Fact]
    public async Task CleanupIdleResourcesAsync_UnsubscribesIdleTopics()
    {
        var publisher = new TestPubSubPublisher();
        var subscriber = new TestPubSubSubscriber();
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            OffloadTopicPattern = "{topic}.offload.{tenantId}",
            LaneIdleTimeout = TimeSpan.FromSeconds(1)
        };

        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, subscriber, options, fakeTime);

        var workItem = new TenantOffloadWorkItem(
            TenantId: "tenant-reap",
            PubSubName: "pubsub",
            Topic: "orders.events",
            Payload: new byte[] { 1 },
            Headers: new Dictionary<string, string>(),
            HandlerInvoker: _ => ValueTask.FromResult(EventHandlingResult.Success),
            CreatedAt: fakeTime.GetUtcNow());

        await strategy.ExecuteOffloadAsync(workItem, CancellationToken.None);
        Assert.Single(strategy.ActiveOffloadTopics);

        // Advance past idle timeout
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        await strategy.CleanupIdleResourcesAsync(CancellationToken.None);

        Assert.Single(subscriber.Unsubscriptions);
        Assert.Equal("pubsub", subscriber.Unsubscriptions[0].PubSubName);
        Assert.Equal("orders.events.offload.tenant-reap", subscriber.Unsubscriptions[0].Topic);
        Assert.Empty(strategy.ActiveOffloadTopics);
    }
}
