namespace Centra.Core.Actors;

/// <summary>
/// Default implementation of <see cref="IConsistentHashRingBuilder"/>.
/// </summary>
public sealed class ConsistentHashRingBuilder : IConsistentHashRingBuilder
{
    /// <summary>
    /// Singleton default instance of <see cref="ConsistentHashRingBuilder"/>.
    /// </summary>
    public static readonly ConsistentHashRingBuilder Instance = new();

    public ConsistentHashRingState BuildRing(IReadOnlyCollection<string> nodes, int virtualNodesPerNode, IConsistentHashAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(algorithm);

        var entries = new List<(uint Hash, string NodeId)>(nodes.Count * virtualNodesPerNode);

        foreach (var node in nodes)
        {
            for (var i = 0; i < virtualNodesPerNode; i++)
            {
                var vNodeKey = $"{node}#vnode{i}";
                var hash = algorithm.ComputeHash(vNodeKey);
                entries.Add((hash, node));
            }
        }

        entries.Sort((a, b) => a.Hash.CompareTo(b.Hash));

        var hashes = new uint[entries.Count];
        var nodeIds = new string[entries.Count];

        for (var i = 0; i < entries.Count; i++)
        {
            hashes[i] = entries[i].Hash;
            nodeIds[i] = entries[i].NodeId;
        }

        return new ConsistentHashRingState(hashes, nodeIds);
    }
}
