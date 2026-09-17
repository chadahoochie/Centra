using Centra.PubSub.Tenancy;
using Xunit;

namespace Centra.Tests.Unit.Tenancy;

public sealed class EphemeralTopicTurnTrackerTests
{
    [Fact]
    public void TurnTracker_Tracks_Increments_And_Decrements_Correctly()
    {
        var tracker = new EphemeralTopicTurnTracker();
        var key = ("pubsub", "tenant.orders.offload.tenant-1");

        Assert.Equal(0, tracker.GetActiveTurns(key));

        tracker.Enter(key);
        Assert.Equal(1, tracker.GetActiveTurns(key));

        tracker.Enter(key);
        Assert.Equal(2, tracker.GetActiveTurns(key));

        tracker.Exit(key);
        Assert.Equal(1, tracker.GetActiveTurns(key));

        tracker.Exit(key);
        Assert.Equal(0, tracker.GetActiveTurns(key));

        // Decrement below zero should clamp at 0
        tracker.Exit(key);
        Assert.Equal(0, tracker.GetActiveTurns(key));
    }

    [Fact]
    public async Task TurnTracker_Handles_Concurrent_Increments_And_Decrements()
    {
        var tracker = new EphemeralTopicTurnTracker();
        var key = ("pubsub", "tenant.orders.offload.tenant-concurrent");

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            tracker.Enter(key);
            tracker.Exit(key);
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.Equal(0, tracker.GetActiveTurns(key));
    }

    [Fact]
    public void TurnTracker_Tracks_Distinct_Topics_Independently()
    {
        var tracker = new EphemeralTopicTurnTracker();
        var key1 = ("pubsub", "tenant.orders.offload.tenant-1");
        var key2 = ("pubsub", "tenant.orders.offload.tenant-2");

        tracker.Enter(key1);
        tracker.Enter(key1);
        tracker.Enter(key2);

        Assert.Equal(2, tracker.GetActiveTurns(key1));
        Assert.Equal(1, tracker.GetActiveTurns(key2));

        tracker.Exit(key1);
        Assert.Equal(1, tracker.GetActiveTurns(key1));
        Assert.Equal(1, tracker.GetActiveTurns(key2));
    }

    [Fact]
    public void TurnTracker_TryRemove_Prunes_Topic_And_Prevents_Memory_Leaks()
    {
        var tracker = new EphemeralTopicTurnTracker();
        var key = ("pubsub", "tenant.orders.offload.tenant-reaped");

        Assert.False(tracker.TryRemove(key));

        tracker.Enter(key);
        tracker.Exit(key);

        Assert.True(tracker.TryRemove(key));
        Assert.Equal(0, tracker.GetActiveTurns(key));
        Assert.False(tracker.TryRemove(key));
    }
}
