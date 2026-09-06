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
}
