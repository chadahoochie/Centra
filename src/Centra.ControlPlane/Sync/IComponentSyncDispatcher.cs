using Centra.Sync;

namespace Centra.ControlPlane.Sync;

public interface IComponentSyncDispatcher
{
    ValueTask PublishEventAsync(ComponentSyncEvent syncEvent, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ComponentSyncEvent> SubscribeAsync(string appId, string instanceId, CancellationToken cancellationToken = default);
    ValueTask BroadcastResilienceUpdateAsync(ResilienceSyncEventDto syncEvent, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResilienceSyncEventDto> SubscribeResilienceAsync(string appId, string instanceId, CancellationToken cancellationToken = default);
}
