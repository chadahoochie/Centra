using Centra.ControlPlane.Tests.Unit.Common;
using Centra.ControlPlane.Topology;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Topology;

public sealed class StateStoreTopologyTrackerTests
{
    [Fact]
    public async Task Should_Record_Heartbeat_And_Track_Active_Node()
    {
        // Arrange
        var stateStore = new FakeStateStore();
        var topology = new StateStoreTopologyTracker(stateStore);
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
    public async Task Should_Evict_Stale_Nodes_When_Cutoff_Exceeded()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var stateStore = new FakeStateStore();
        var topology = new StateStoreTopologyTracker(stateStore, "default", fakeTime, defaultNodeTtl: TimeSpan.FromSeconds(30));

        await topology.RecordHeartbeatAsync(new HeartbeatRequest("svc-a", "inst-1", "Healthy", null));
        await topology.RecordHeartbeatAsync(new HeartbeatRequest("svc-b", "inst-2", "Healthy", null));

        // Advance 25 seconds
        fakeTime.Advance(TimeSpan.FromSeconds(25));
        // svc-b sends heartbeat at T0 + 25s
        await topology.RecordHeartbeatAsync(new HeartbeatRequest("svc-b", "inst-2", "Healthy", null));

        // Advance another 10 seconds (Total 35s from start)
        // svc-a last heartbeat was at T0 (35s ago > 30s threshold -> should be evicted)
        // svc-b last heartbeat was at T0 + 25s (10s ago < 30s threshold -> should remain active)
        fakeTime.Advance(TimeSpan.FromSeconds(10));

        // Act
        var activeNodes = await topology.GetActiveNodesAsync();

        // Assert
        activeNodes.Count.ShouldBe(1);
        activeNodes.ShouldNotContain(n => n.AppId == "svc-a" && n.InstanceId == "inst-1");
        activeNodes.ShouldContain(n => n.AppId == "svc-b" && n.InstanceId == "inst-2");
    }

    [Fact]
    public async Task Should_Evict_Stale_Nodes_Explicitly()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var stateStore = new FakeStateStore();
        var topology = new StateStoreTopologyTracker(stateStore, "default", fakeTime);
        await topology.RecordHeartbeatAsync(new HeartbeatRequest("svc-x", "inst-99", "Healthy", null));

        // Advance 40s
        fakeTime.Advance(TimeSpan.FromSeconds(40));

        // Act
        await topology.EvictStaleNodesAsync(TimeSpan.FromSeconds(30));
        var node = await topology.GetNodeAsync("svc-x", "inst-99");
        var active = await topology.GetActiveNodesAsync();

        // Assert
        node.ShouldBeNull();
        active.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Persist_Across_Tracker_Reinstantiation()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var stateStore = new FakeStateStore();
        var tracker1 = new StateStoreTopologyTracker(stateStore, "default", fakeTime);

        await tracker1.RecordHeartbeatAsync(new HeartbeatRequest("persist-svc", "inst-1", "Healthy", null));

        // Act - Reinstantiate tracker with same state store
        var tracker2 = new StateStoreTopologyTracker(stateStore, "default", fakeTime);
        var node = await tracker2.GetNodeAsync("persist-svc", "inst-1");
        var active = await tracker2.GetActiveNodesAsync();

        // Assert
        node.ShouldNotBeNull();
        node.AppId.ShouldBe("persist-svc");
        node.InstanceId.ShouldBe("inst-1");
        active.Count.ShouldBe(1);
    }

    [Fact]
    public void Should_Throw_When_StateStore_Is_Null()
    {
        Should.Throw<ArgumentNullException>(() => new StateStoreTopologyTracker(null!));
    }
}
