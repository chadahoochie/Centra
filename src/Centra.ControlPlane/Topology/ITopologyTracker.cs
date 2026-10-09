namespace Centra.ControlPlane.Topology;

public interface ITopologyTracker
{
    ValueTask<HeartbeatResponse> RecordHeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<ClientNodeInfo>> GetActiveNodesAsync(CancellationToken cancellationToken = default)
        => GetActiveNodesAsync(clusterId: null, cancellationToken);
    ValueTask<IReadOnlyCollection<ClientNodeInfo>> GetActiveNodesAsync(string? clusterId, CancellationToken cancellationToken = default);
    ValueTask<ClientNodeInfo?> GetNodeAsync(string appId, string instanceId, string? clusterId = null, CancellationToken cancellationToken = default);
    ValueTask EvictStaleNodesAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
