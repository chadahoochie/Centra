using Centra.ControlPlane.Topology;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Topology;

public sealed class MultiClusterTopologyTests
{
    [Fact]
    public async Task Should_Partition_Active_Nodes_By_ClusterId()
    {
        // Arrange
        var topology = new InMemoryTopologyTracker();
        var cluster1Node1 = new HeartbeatRequest("svc-a", "inst-1", "Healthy", null, "cluster-1");
        var cluster1Node2 = new HeartbeatRequest("svc-b", "inst-2", "Healthy", null, "cluster-1");
        var cluster2Node1 = new HeartbeatRequest("svc-c", "inst-3", "Healthy", null, "cluster-2");
        var defaultClusterNode = new HeartbeatRequest("svc-d", "inst-4", "Healthy", null);

        // Act
        await topology.RecordHeartbeatAsync(cluster1Node1);
        await topology.RecordHeartbeatAsync(cluster1Node2);
        await topology.RecordHeartbeatAsync(cluster2Node1);
        await topology.RecordHeartbeatAsync(defaultClusterNode);

        var cluster1Nodes = await topology.GetActiveNodesAsync("cluster-1");
        var cluster2Nodes = await topology.GetActiveNodesAsync("cluster-2");
        var defaultClusterNodes = await topology.GetActiveNodesAsync("default");
        var allNodes = await topology.GetActiveNodesAsync();

        // Assert
        cluster1Nodes.Count.ShouldBe(2);
        cluster1Nodes.ShouldAllBe(n => n.ClusterId == "cluster-1");

        cluster2Nodes.Count.ShouldBe(1);
        cluster2Nodes.First().AppId.ShouldBe("svc-c");
        cluster2Nodes.First().ClusterId.ShouldBe("cluster-2");

        defaultClusterNodes.Count.ShouldBe(1);
        defaultClusterNodes.First().AppId.ShouldBe("svc-d");
        defaultClusterNodes.First().ClusterId.ShouldBe("default");

        allNodes.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Should_Lookup_Node_By_ClusterId()
    {
        // Arrange
        var topology = new InMemoryTopologyTracker();
        await topology.RecordHeartbeatAsync(new HeartbeatRequest("order-svc", "inst-1", "Healthy", null, "cluster-alpha"));
        await topology.RecordHeartbeatAsync(new HeartbeatRequest("order-svc", "inst-1", "Healthy", null, "cluster-beta"));

        // Act
        var alphaNode = await topology.GetNodeAsync("order-svc", "inst-1", "cluster-alpha");
        var betaNode = await topology.GetNodeAsync("order-svc", "inst-1", "cluster-beta");
        var gammaNode = await topology.GetNodeAsync("order-svc", "inst-1", "cluster-gamma");

        // Assert
        alphaNode.ShouldNotBeNull();
        alphaNode.ClusterId.ShouldBe("cluster-alpha");

        betaNode.ShouldNotBeNull();
        betaNode.ClusterId.ShouldBe("cluster-beta");

        gammaNode.ShouldBeNull();
    }
}
