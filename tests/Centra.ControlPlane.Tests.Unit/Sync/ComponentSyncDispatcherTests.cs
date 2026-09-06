using Centra.Components;
using Centra.ControlPlane.Sync;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Sync;

public sealed class ComponentSyncDispatcherTests
{
    [Fact]
    public async Task Should_Broadcast_Events_To_Active_Subscribers()
    {
        // Arrange
        var dispatcher = new ComponentSyncDispatcher();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var def = new ComponentDefinition
        {
            Name = "pubsub-broker",
            Type = ComponentType.PubSub,
            Provider = "in-memory"
        };
        var syncEvent = new ComponentSyncEvent(ComponentSyncEventType.Added, def, "pubsub-broker", 1, DateTimeOffset.UtcNow);

        var receivedEvents = new List<ComponentSyncEvent>();

        // Act: Start subscriber in background
        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var evt in dispatcher.SubscribeAsync("test-app", "inst-1", cts.Token))
            {
                receivedEvents.Add(evt);
                break; // Stop after first event
            }
        });

        // Deterministically wait until subscriber is registered
        while (dispatcher.SubscriberCount == 0 && !cts.IsCancellationRequested)
        {
            await Task.Delay(10);
        }

        // Publish event
        await dispatcher.PublishEventAsync(syncEvent);

        await subscriberTask;

        // Assert
        receivedEvents.Count.ShouldBe(1);
        receivedEvents[0].Type.ShouldBe(ComponentSyncEventType.Added);
        receivedEvents[0].ComponentName.ShouldBe("pubsub-broker");
    }
}
