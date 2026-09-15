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
}
