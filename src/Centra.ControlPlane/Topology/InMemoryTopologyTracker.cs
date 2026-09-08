using System.Collections.Concurrent;

namespace Centra.ControlPlane.Topology;

public sealed class InMemoryTopologyTracker : ITopologyTracker, IDisposable
{
    private readonly ConcurrentDictionary<string, ClientNodeInfo> _nodes = new(StringComparer.OrdinalIgnoreCase);
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

        var key = $"{request.AppId}:{request.InstanceId}";
        var now = _timeProvider.GetUtcNow();

        _nodes.AddOrUpdate(
            key,
            _ => new ClientNodeInfo(request.AppId, request.InstanceId, request.Status, now, now, request.Metadata),
            (_, existing) => existing with
            {
                Status = request.Status,
                LastHeartbeatUtc = now,
                Metadata = request.Metadata ?? existing.Metadata
            });

        return ValueTask.FromResult(new HeartbeatResponse(true, now));
    }

    public ValueTask<IReadOnlyCollection<ClientNodeInfo>> GetActiveNodesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ClientNodeInfo> nodes = _nodes.Values.ToArray();
        return ValueTask.FromResult(nodes);
    }

    public ValueTask<ClientNodeInfo?> GetNodeAsync(string appId, string instanceId, CancellationToken cancellationToken = default)
    {
        var key = $"{appId}:{instanceId}";
        _nodes.TryGetValue(key, out var node);
        return ValueTask.FromResult(node);
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
