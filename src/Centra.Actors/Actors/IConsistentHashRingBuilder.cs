namespace Centra.Core.Actors;

/// <summary>
/// Defines a contract for building consistent hash ring partition snapshots.
/// </summary>
public interface IConsistentHashRingBuilder
{
    /// <summary>
    /// Builds a sorted snapshot of virtual node hashes and their corresponding node IDs.
    /// </summary>
    ConsistentHashRingState BuildRing(IReadOnlyCollection<string> nodes, int virtualNodesPerNode, IConsistentHashAlgorithm algorithm);
}
