using Centra.Components;

namespace Centra.Sync;

public interface IControlPlaneClient
{
    Task<IReadOnlyCollection<ComponentDefinition>> GetComponentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ServiceNodeDto>> GetTopologyAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ServiceNodeDto>> GetTopologyAsync(string? clusterId, CancellationToken cancellationToken = default)
        => GetTopologyAsync(cancellationToken);
    Task<IReadOnlyCollection<ResiliencePolicyDto>> GetResiliencePoliciesAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<ComponentSyncEventDto> StreamUpdatesAsync(string appId, string instanceId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResilienceSyncEventDto> StreamResilienceUpdatesAsync(string appId, string instanceId, CancellationToken cancellationToken = default);
    Task SendHeartbeatAsync(string appId, string instanceId, string status, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
    Task SendHeartbeatAsync(string appId, string instanceId, string status, IReadOnlyDictionary<string, string>? metadata, string clusterId, CancellationToken cancellationToken = default)
        => SendHeartbeatAsync(appId, instanceId, status, metadata, cancellationToken);
}
