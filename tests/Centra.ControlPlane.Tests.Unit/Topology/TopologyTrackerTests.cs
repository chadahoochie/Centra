using Centra.ControlPlane.Topology;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Topology;

public sealed class TopologyTrackerTests
{
    [Fact]
    public async Task Should_Record_Heartbeat_And_Track_Active_Node()
    {
        // Arrange
        var topology = new InMemoryTopologyTracker();
        var request = new HeartbeatRequest("orders-svc", "inst-1", "Healthy", new Dictionary<string, string> { ["host"] = "pod-a" });

        // Act
        var response = await topology.RecordHeartbeatAsync(request);
        var nodes = await topology.GetActiveNodesAsync();
        var node = await topology.GetNodeAsync("orders-svc", "inst-1");

        // Assert
        response.Acknowledged.ShouldBeTrue();
        nodes.Count.ShouldBe(1);
        node.ShouldNotBeNull();
        node.AppId.ShouldBe("orders-svc");
        node.InstanceId.ShouldBe("inst-1");
        node.Status.ShouldBe("Healthy");
        node.Metadata.ShouldNotBeNull();
        node.Metadata["host"].ShouldBe("pod-a");
    }

    [Fact]
    public async Task Should_Update_Heartbeat_Timestamp_On_Subsequent_Pings()
    {
        // Arrange
        var topology = new InMemoryTopologyTracker();
        await topology.RecordHeartbeatAsync(new HeartbeatRequest("orders-svc", "inst-1", "Healthy", null));

        // Act
        var ping2 = await topology.RecordHeartbeatAsync(new HeartbeatRequest("orders-svc", "inst-1", "Degraded", null));
        var node = await topology.GetNodeAsync("orders-svc", "inst-1");

        // Assert
        node.ShouldNotBeNull();
        node.Status.ShouldBe("Degraded");
    }

    [Fact]
    public async Task Should_Evict_Stale_Nodes_When_Cutoff_Exceeded()
    {
        // Arrange
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var topology = new InMemoryTopologyTracker(fakeTime);

        await topology.RecordHeartbeatAsync(new HeartbeatRequest("svc-a", "inst-1", "Healthy", null));
        await topology.RecordHeartbeatAsync(new HeartbeatRequest("svc-b", "inst-2", "Healthy", null));

        // Advance 25 seconds
        fakeTime.Advance(TimeSpan.FromSeconds(25));
        // svc-b sends heartbeat at T0 + 25s
        await topology.RecordHeartbeatAsync(new HeartbeatRequest("svc-b", "inst-2", "Healthy", null));

        // Advance another 10 seconds (total 35s since svc-a heartbeat, 10s since svc-b heartbeat)
        fakeTime.Advance(TimeSpan.FromSeconds(10));

        // Act: Evict nodes older than 30s
        await topology.EvictStaleNodesAsync(TimeSpan.FromSeconds(30));

        // Assert
        var activeNodes = await topology.GetActiveNodesAsync();
        activeNodes.Count.ShouldBe(1);

        var nodeA = await topology.GetNodeAsync("svc-a", "inst-1");
        nodeA.ShouldBeNull();

        var nodeB = await topology.GetNodeAsync("svc-b", "inst-2");
        nodeB.ShouldNotBeNull();
        nodeB.InstanceId.ShouldBe("inst-2");
    }

    [Fact]
    public async Task Should_Periodically_Evict_Stale_Nodes_Via_Timer()
    {
        // Arrange
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using var topology = new InMemoryTopologyTracker(
            timeProvider: fakeTime,
            staleTimeout: TimeSpan.FromSeconds(30),
            evictionInterval: TimeSpan.FromSeconds(10));

        await topology.RecordHeartbeatAsync(new HeartbeatRequest("svc-a", "inst-1", "Healthy", null));

        var initialNodes = await topology.GetActiveNodesAsync();
        initialNodes.Count.ShouldBe(1);

        // Act: Advance time past staleTimeout (30s) and evictionInterval (10s)
        fakeTime.Advance(TimeSpan.FromSeconds(35));

        // Assert: Node should have been automatically evicted by the timer
        var remainingNodes = await topology.GetActiveNodesAsync();
        remainingNodes.Count.ShouldBe(0);
    }
}
