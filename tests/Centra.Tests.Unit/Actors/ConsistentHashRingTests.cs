using Centra.Actors;
using Centra.Core.Actors;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ConsistentHashRingTests
{
    [Fact]
    public void Should_Throw_When_No_Nodes_Are_Registered()
    {
        var ring = new ConsistentHashRing();

        Should.Throw<InvalidOperationException>(() => ring.GetNode("any-key"));
    }

    [Fact]
    public void Should_Return_Single_Node_When_Only_One_Node_Registered()
    {
        var ring = new ConsistentHashRing();
        ring.AddNode("node-1");

        ring.GetNode("device-101").ShouldBe("node-1");
        ring.GetNode("device-202").ShouldBe("node-1");
        ring.GetNode("account-303").ShouldBe("node-1");
    }

    [Fact]
    public void Should_Be_Deterministic_For_Same_Key()
    {
        var ring = new ConsistentHashRing();
        ring.AddNode("node-1");
        ring.AddNode("node-2");
        ring.AddNode("node-3");

        var targetNode = ring.GetNode("order-9999");

        for (var i = 0; i < 50; i++)
        {
            ring.GetNode("order-9999").ShouldBe(targetNode);
        }
    }

    [Fact]
    public void Should_Distribute_Keys_Fairly_Across_Nodes()
    {
        var ring = new ConsistentHashRing(virtualNodesPerNode: 150);
        var nodes = new[] { "node-1", "node-2", "node-3" };
        foreach (var node in nodes)
        {
            ring.AddNode(node);
        }

        var counts = new Dictionary<string, int>
        {
            ["node-1"] = 0,
            ["node-2"] = 0,
            ["node-3"] = 0
        };

        const int totalKeys = 3000;
        for (var i = 0; i < totalKeys; i++)
        {
            var key = $"sensor-{i}";
            var selected = ring.GetNode(key);
            counts[selected]++;
        }

        // Each node should hold between 20% and 45% of total keys
        foreach (var (node, count) in counts)
        {
            var ratio = (double)count / totalKeys;
            ratio.ShouldBeGreaterThan(0.20, $"Node {node} received too few keys: {ratio:P}");
            ratio.ShouldBeLessThan(0.45, $"Node {node} received too many keys: {ratio:P}");
        }
    }

    [Fact]
    public void Should_Handle_Node_Removal_And_Rebalance_Keys()
    {
        var ring = new ConsistentHashRing();
        ring.AddNode("node-1");
        ring.AddNode("node-2");
        ring.AddNode("node-3");

        ring.RemoveNode("node-2");

        for (var i = 0; i < 100; i++)
        {
            var node = ring.GetNode($"key-{i}");
            node.ShouldBeOneOf("node-1", "node-3");
        }
    }

    [Fact]
    public void ActorPlacementDirector_Should_Accurately_Identify_Local_Vs_Remote_Node()
    {
        var ring = new ConsistentHashRing();
        ring.AddNode("local-instance");
        ring.AddNode("remote-instance");

        var director = new ActorPlacementDirector("local-instance", ring);

        // Find a key that maps to local
        var localKey = Enumerable.Range(0, 500)
            .Select(i => new ActorIdentity("TestActor", $"local-candidate-{i}"))
            .First(id => ring.GetNode(id.ToString()) == "local-instance");

        director.IsLocal(localKey).ShouldBeTrue();
        director.ResolveNodeId(localKey).ShouldBe("local-instance");

        // Find a key that maps to remote
        var remoteKey = Enumerable.Range(0, 500)
            .Select(i => new ActorIdentity("TestActor", $"remote-candidate-{i}"))
            .First(id => ring.GetNode(id.ToString()) == "remote-instance");

        director.IsLocal(remoteKey).ShouldBeFalse();
        director.ResolveNodeId(remoteKey).ShouldBe("remote-instance");
    }
}
