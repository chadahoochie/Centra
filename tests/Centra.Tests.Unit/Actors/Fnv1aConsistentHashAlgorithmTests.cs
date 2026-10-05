using Centra.Core.Actors;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class Fnv1aConsistentHashAlgorithmTests
{
    [Fact]
    public void ComputeHash_Should_Throw_ArgumentNullException_When_Key_Is_Null()
    {
        var sut = Fnv1aConsistentHashAlgorithm.Instance;

        Should.Throw<ArgumentNullException>(() => sut.ComputeHash(null!));
    }

    [Fact]
    public void ComputeHash_Should_Return_OffsetBasis_For_Empty_String()
    {
        var sut = Fnv1aConsistentHashAlgorithm.Instance;

        var hash = sut.ComputeHash(string.Empty);

        hash.ShouldBe(2166136261u);
    }

    [Fact]
    public void ComputeHash_Should_Hash_Short_Key_Using_Stackalloc()
    {
        var sut = Fnv1aConsistentHashAlgorithm.Instance;
        var key = "short-actor-key-456";

        var hash1 = sut.ComputeHash(key);
        var hash2 = sut.ComputeHash(key);

        hash1.ShouldBe(hash2);
        hash1.ShouldNotBe(0u);
        hash1.ShouldNotBe(2166136261u);
    }

    [Fact]
    public void ComputeHash_Should_Hash_Long_Key_Exceeding_256_Bytes()
    {
        var sut = Fnv1aConsistentHashAlgorithm.Instance;
        var longKey = new string('Z', 350);

        var hash1 = sut.ComputeHash(longKey);
        var hash2 = sut.ComputeHash(longKey);

        hash1.ShouldBe(hash2);
        hash1.ShouldNotBe(0u);
    }

    [Fact]
    public void ComputeHash_Should_Produce_Different_Hashes_For_Different_Keys()
    {
        var sut = Fnv1aConsistentHashAlgorithm.Instance;

        var hash1 = sut.ComputeHash("actor-order-1");
        var hash2 = sut.ComputeHash("actor-order-2");

        hash1.ShouldNotBe(hash2);
    }

    [Fact]
    public void ConsistentHashRing_Should_Route_With_Fnv1aAlgorithm()
    {
        var ring = new ConsistentHashRing(virtualNodesPerNode: 32, hashAlgorithm: Fnv1aConsistentHashAlgorithm.Instance);
        ring.AddNode("node-a");
        ring.AddNode("node-b");
        ring.AddNode("node-c");

        var node1 = ring.GetNode("actor-key-1");
        var node2 = ring.GetNode("actor-key-1");
        var node3 = ring.GetNode("actor-key-999");

        node1.ShouldBe(node2);
        node1.ShouldBeOneOf("node-a", "node-b", "node-c");
        node3.ShouldBeOneOf("node-a", "node-b", "node-c");
    }
}
