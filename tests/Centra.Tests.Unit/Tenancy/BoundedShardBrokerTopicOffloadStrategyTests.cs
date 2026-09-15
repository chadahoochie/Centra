using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Xunit;

namespace Centra.Tests.Unit.Tenancy;

public sealed class BoundedShardBrokerTopicOffloadStrategyTests
{
    [Fact]
    public async Task ExecuteOffloadAsync_HashesIntoBoundedShardTopics()
    {
        var publisher = new TestPubSubPublisher();
        var options = new TenantOffloadOptions
        {
            OffloadShardCount = 4
        };

        var strategy = new BoundedShardBrokerTopicOffloadStrategy(publisher, options);

        var tenants = new[] { "tenant-A", "tenant-B", "tenant-C", "tenant-D", "tenant-E" };

        foreach (var tenant in tenants)
        {
            var workItem = new TenantOffloadWorkItem(
                TenantId: tenant,
                PubSubName: "pubsub",
                Topic: "orders",
                Payload: ReadOnlyMemory<byte>.Empty,
                Headers: new Dictionary<string, string>(),
                HandlerInvoker: _ => ValueTask.FromResult(EventHandlingResult.Success),
                CreatedAt: DateTimeOffset.UtcNow);

            var result = await strategy.ExecuteOffloadAsync(workItem, CancellationToken.None);
            Assert.Equal(EventHandlingResult.Success, result);
        }

        Assert.Equal(tenants.Length, publisher.PublishedMessages.Count);

        foreach (var published in publisher.PublishedMessages)
        {
            Assert.StartsWith("orders.offload.", published.Topic, StringComparison.Ordinal);
            var shardStr = published.Topic.Replace("orders.offload.", "", StringComparison.Ordinal);
            var shardId = int.Parse(shardStr);
            Assert.InRange(shardId, 0, 3);
            Assert.Equal("true", published.Metadata["ce-offloaded"]);
        }
    }
}
