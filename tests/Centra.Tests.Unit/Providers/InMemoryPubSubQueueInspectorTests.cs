using Centra.Providers.InMemory.PubSub;
using Centra.PubSub;
using Xunit;

namespace Centra.Tests.Unit.Providers;

public sealed class InMemoryPubSubQueueInspectorTests
{
    [Fact]
    public async Task GetQueueStatsAsync_Reports_ConsumerCount_Correctly()
    {
        var driver = new InMemoryPubSubDriver();
        var inspector = (IPubSubQueueInspector)driver;

        var statsBefore = await inspector.GetQueueStatsAsync("pubsub", "orders.test");
        Assert.NotNull(statsBefore);
        Assert.Equal(0, statsBefore.Value.ConsumerCount);
        Assert.Equal(0, statsBefore.Value.MessageCount);

        await driver.SubscribeAsync("pubsub", "orders.test", (_, _, _) => ValueTask.FromResult(EventHandlingResult.Success));

        var statsAfter = await inspector.GetQueueStatsAsync("pubsub", "orders.test");
        Assert.NotNull(statsAfter);
        Assert.Equal(1, statsAfter.Value.ConsumerCount);
        Assert.Equal(0, statsAfter.Value.MessageCount);

        await driver.UnsubscribeAsync("pubsub", "orders.test");

        var statsUnsub = await inspector.GetQueueStatsAsync("pubsub", "orders.test");
        Assert.NotNull(statsUnsub);
        Assert.Equal(0, statsUnsub.Value.ConsumerCount);
    }
}
