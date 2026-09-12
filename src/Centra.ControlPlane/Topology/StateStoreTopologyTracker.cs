using Centra.State;

namespace Centra.ControlPlane.Topology;

/// <summary>
/// Persistent implementation of <see cref="ITopologyTracker"/> backed by any configured <see cref="IStateStore"/>
/// with TTL-based node heartbeat expiration.
/// </summary>
public sealed class StateStoreTopologyTracker : ITopologyTracker
{
    private const string IndexKey = "centra:controlplane:topology:index";
    private readonly IStateStore _stateStore;
    private readonly string _storeName;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _defaultNodeTtl;

    public StateStoreTopologyTracker(
        IStateStore stateStore,
        string storeName = "default",
        TimeProvider? timeProvider = null,
        TimeSpan? defaultNodeTtl = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _storeName = storeName;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _defaultNodeTtl = defaultNodeTtl ?? TimeSpan.FromSeconds(30);
    }

    public async ValueTask<HeartbeatResponse> RecordHeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstanceId);

        var key = $"centra:controlplane:topology:{request.AppId.ToLowerInvariant()}:{request.InstanceId.ToLowerInvariant()}";
        var now = _timeProvider.GetUtcNow();

        var existing = await _stateStore.GetAsync<ClientNodeInfo>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        var registeredAt = existing.HasValue ? existing.Value.Value.RegisteredAtUtc : now;

        var nodeInfo = new ClientNodeInfo(
            request.AppId,
            request.InstanceId,
            request.Status,
            registeredAt,
            now,
            request.Metadata ?? (existing.HasValue ? existing.Value.Value.Metadata : null));

        var options = new StateOptions { TimeToLive = _defaultNodeTtl };
        await _stateStore.SetAsync(_storeName, key, nodeInfo, options, cancellationToken).ConfigureAwait(false);

        // Update topology index
        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var indexEntry = await _stateStore.GetAsync<List<string>>(_storeName, IndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            var list = indexEntry.HasValue ? new List<string>(indexEntry.Value.Value) : [];
            var nodeCompositeKey = $"{request.AppId.ToLowerInvariant()}:{request.InstanceId.ToLowerInvariant()}";
            if (!list.Contains(nodeCompositeKey))
            {
                list.Add(nodeCompositeKey);
            }

            var etag = indexEntry?.ETag ?? string.Empty;
            var success = await _stateStore.TrySetAsync(_storeName, IndexKey, list, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (success)
            {
                break;
            }
        }

        return new HeartbeatResponse(true, now);
    }

    public async ValueTask<IReadOnlyCollection<ClientNodeInfo>> GetActiveNodesAsync(CancellationToken cancellationToken = default)
    {
        var indexEntry = await _stateStore.GetAsync<List<string>>(_storeName, IndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!indexEntry.HasValue || indexEntry.Value.Value.Count == 0)
        {
            return Array.Empty<ClientNodeInfo>();
        }

        var result = new List<ClientNodeInfo>();
        var staleKeys = new List<string>();
        var cutoff = _timeProvider.GetUtcNow().Subtract(_defaultNodeTtl);

        foreach (var compositeKey in indexEntry.Value.Value)
        {
            var key = $"centra:controlplane:topology:{compositeKey}";
            var entry = await _stateStore.GetAsync<ClientNodeInfo>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (entry.HasValue && entry.Value.Value.LastHeartbeatUtc >= cutoff)
            {
                result.Add(entry.Value.Value);
            }
            else
            {
                staleKeys.Add(compositeKey);
            }
        }

        // Clean up stale nodes from index if any expired
        if (staleKeys.Count > 0)
        {
            var list = new List<string>(indexEntry.Value.Value);
            foreach (var stale in staleKeys)
            {
                list.Remove(stale);
            }
            await _stateStore.TrySetAsync(_storeName, IndexKey, list, indexEntry.Value.ETag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async ValueTask<ClientNodeInfo?> GetNodeAsync(string appId, string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var key = $"centra:controlplane:topology:{appId.ToLowerInvariant()}:{instanceId.ToLowerInvariant()}";
        var entry = await _stateStore.GetAsync<ClientNodeInfo>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        return entry.HasValue ? entry.Value.Value : null;
    }

    public async ValueTask EvictStaleNodesAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var indexEntry = await _stateStore.GetAsync<List<string>>(_storeName, IndexKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!indexEntry.HasValue || indexEntry.Value.Value.Count == 0)
        {
            return;
        }

        var cutoff = _timeProvider.GetUtcNow().Subtract(timeout);
        var staleKeys = new List<string>();

        foreach (var compositeKey in indexEntry.Value.Value)
        {
            var key = $"centra:controlplane:topology:{compositeKey}";
            var entry = await _stateStore.GetAsync<ClientNodeInfo>(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!entry.HasValue || entry.Value.Value.LastHeartbeatUtc < cutoff)
            {
                staleKeys.Add(compositeKey);
                await _stateStore.DeleteAsync(_storeName, key, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        if (staleKeys.Count > 0)
        {
            var list = new List<string>(indexEntry.Value.Value);
            foreach (var stale in staleKeys)
            {
                list.Remove(stale);
            }
            await _stateStore.TrySetAsync(_storeName, IndexKey, list, indexEntry.Value.ETag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
