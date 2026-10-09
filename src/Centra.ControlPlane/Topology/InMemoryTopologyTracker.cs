using System.Collections.Concurrent;

namespace Centra.ControlPlane.Topology;

public sealed class InMemoryTopologyTracker : ITopologyTracker, IDisposable
{
    private readonly ConcurrentDictionary<CompositeClusterKey, ClientNodeInfo> _nodes = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _staleTimeout;
    private readonly ITimer? _evictionTimer;

    public InMemoryTopologyTracker(
        TimeProvider? timeProvider = null,
        TimeSpan? staleTimeout = null,
        TimeSpan? evictionInterval = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _staleTimeout = staleTimeout ?? TimeSpan.FromSeconds(30);

        if (evictionInterval.HasValue && evictionInterval.Value > TimeSpan.Zero)
        {
            _evictionTimer = _timeProvider.CreateTimer(
                static state =>
                {
                    var self = (InMemoryTopologyTracker)state!;
                    _ = self.EvictStaleNodesAsync(self._staleTimeout);
                },
                this,
                evictionInterval.Value,
                evictionInterval.Value);
        }
    }

    public ValueTask<HeartbeatResponse> RecordHeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstanceId);

        var clusterId = string.IsNullOrWhiteSpace(request.ClusterId) ? "default" : request.ClusterId;
        var key = new CompositeClusterKey(clusterId, request.AppId, request.InstanceId);
        var now = _timeProvider.GetUtcNow();

        _nodes.AddOrUpdate(
            key,
            _ => new ClientNodeInfo(request.AppId, request.InstanceId, request.Status, now, now, request.Metadata, clusterId),
            (_, existing) => existing with
            {
                Status = request.Status,
                LastHeartbeatUtc = now,
                Metadata = request.Metadata ?? existing.Metadata
            });

        return ValueTask.FromResult(new HeartbeatResponse(true, now));
    }

    public ValueTask<IReadOnlyCollection<ClientNodeInfo>> GetActiveNodesAsync(CancellationToken cancellationToken = default)
        => GetActiveNodesAsync(clusterId: null, cancellationToken);

    public ValueTask<IReadOnlyCollection<ClientNodeInfo>> GetActiveNodesAsync(string? clusterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
        {
            IReadOnlyCollection<ClientNodeInfo> all = _nodes.Values.ToArray();
            return ValueTask.FromResult(all);
        }

        var filtered = new List<ClientNodeInfo>();
        foreach (var (key, node) in _nodes)
        {
            if (string.Equals(key.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(node);
            }
        }

        IReadOnlyCollection<ClientNodeInfo> result = filtered;
        return ValueTask.FromResult(result);
    }

    public ValueTask<ClientNodeInfo?> GetNodeAsync(string appId, string instanceId, string? clusterId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(clusterId))
        {
            var key = new CompositeClusterKey(clusterId, appId, instanceId);
            _nodes.TryGetValue(key, out var node);
            return ValueTask.FromResult(node);
        }

        foreach (var (key, node) in _nodes)
        {
            if (string.Equals(key.AppId, appId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(key.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult<ClientNodeInfo?>(node);
            }
        }

        return ValueTask.FromResult<ClientNodeInfo?>(null);
    }

    public ValueTask EvictStaleNodesAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow().Subtract(timeout);
        foreach (var (key, node) in _nodes)
        {
            if (node.LastHeartbeatUtc < cutoff)
            {
                _nodes.TryRemove(key, out _);
            }
        }
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _evictionTimer?.Dispose();
    }
}
