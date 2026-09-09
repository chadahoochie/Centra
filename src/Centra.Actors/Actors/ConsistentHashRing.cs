namespace Centra.Core.Actors;

/// <summary>
/// High-performance, lock-free consistent hash ring using virtual nodes for uniform actor distribution.
/// </summary>
public sealed class ConsistentHashRing
{
    private readonly int _virtualNodesPerNode;
    private readonly IConsistentHashAlgorithm _hashAlgorithm;
    private readonly IConsistentHashRingBuilder _ringBuilder;
    private readonly object _syncLock = new();
    private readonly HashSet<string> _nodes = new(StringComparer.Ordinal);
    private ConsistentHashRingState _state = new(Array.Empty<uint>(), Array.Empty<string>());

    public ConsistentHashRing(
        int virtualNodesPerNode = 100,
        IConsistentHashAlgorithm? hashAlgorithm = null,
        IConsistentHashRingBuilder? ringBuilder = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(virtualNodesPerNode);
        _virtualNodesPerNode = virtualNodesPerNode;
        _hashAlgorithm = hashAlgorithm ?? Md5ConsistentHashAlgorithm.Instance;
        _ringBuilder = ringBuilder ?? ConsistentHashRingBuilder.Instance;
    }

    public void AddNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        lock (_syncLock)
        {
            if (_nodes.Add(nodeId))
            {
                RebuildRing();
            }
        }
    }

    public void RemoveNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        lock (_syncLock)
        {
            if (_nodes.Remove(nodeId))
            {
                RebuildRing();
            }
        }
    }

    public string GetNode(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var state = _state;
        if (state.Hashes.Length == 0)
        {
            throw new InvalidOperationException("No nodes registered in consistent hash ring.");
        }

        var hash = _hashAlgorithm.ComputeHash(key);
        var idx = Array.BinarySearch(state.Hashes, hash);

        if (idx < 0)
        {
            idx = ~idx;
            if (idx >= state.Hashes.Length)
            {
                idx = 0; // Wrap around ring
            }
        }

        return state.NodeIds[idx];
    }

    /// <summary>
    /// Rebuilds the consistent hash ring partition state from active nodes.
    /// </summary>
    public void RebuildRing()
    {
        _state = _ringBuilder.BuildRing(_nodes, _virtualNodesPerNode, _hashAlgorithm);
    }
}
