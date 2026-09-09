using System.Security.Cryptography;
using System.Text;

namespace Centra.Core.Actors;

/// <summary>
/// High-performance, lock-free consistent hash ring using virtual nodes for uniform actor distribution.
/// </summary>
public sealed class ConsistentHashRing
{
    private readonly int _virtualNodesPerNode;
    private readonly object _syncLock = new();
    private readonly HashSet<string> _nodes = new(StringComparer.Ordinal);
    private RingState _state = new(Array.Empty<uint>(), Array.Empty<string>());

    public ConsistentHashRing(int virtualNodesPerNode = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(virtualNodesPerNode);
        _virtualNodesPerNode = virtualNodesPerNode;
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

        var hash = Hash(key);
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

    private void RebuildRing()
    {
        var entries = new List<(uint Hash, string NodeId)>(_nodes.Count * _virtualNodesPerNode);

        foreach (var node in _nodes)
        {
            for (var i = 0; i < _virtualNodesPerNode; i++)
            {
                var vNodeKey = $"{node}#vnode{i}";
                var hash = Hash(vNodeKey);
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

        _state = new RingState(hashes, nodeIds);
    }

    private static uint Hash(string key)
    {
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(key.Length);
        if (maxByteCount <= 256)
        {
            Span<byte> utf8Bytes = stackalloc byte[256];
            var bytesWritten = Encoding.UTF8.GetBytes(key, utf8Bytes);
            Span<byte> hash = stackalloc byte[16];
            MD5.HashData(utf8Bytes[..bytesWritten], hash);
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(key);
            Span<byte> hash = stackalloc byte[16];
            MD5.HashData(bytes, hash);
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash);
        }
    }

    private sealed record RingState(uint[] Hashes, string[] NodeIds);
}
