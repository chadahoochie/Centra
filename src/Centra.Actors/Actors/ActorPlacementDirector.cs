using Centra.Actors;

namespace Centra.Core.Actors;

/// <summary>
/// Directs actor invocations to either the local node or a designated remote node using consistent hashing.
/// </summary>
public sealed class ActorPlacementDirector : IActorPlacementDirector
{
    private readonly string _localNodeId;
    private readonly ConsistentHashRing _ring;

    public ActorPlacementDirector(string localNodeId, ConsistentHashRing ring)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localNodeId);
        ArgumentNullException.ThrowIfNull(ring);

        _localNodeId = localNodeId;
        _ring = ring;
    }

    public string ResolveNodeId(ActorIdentity identity)
    {
        return _ring.GetNode(identity.ToString());
    }

    public bool IsLocal(ActorIdentity identity)
    {
        var targetNode = ResolveNodeId(identity);
        return string.Equals(targetNode, _localNodeId, StringComparison.Ordinal);
    }
}
