using Centra.PubSub;

namespace Centra.PubSub.Tenancy;

public interface ITenantOffloadCoordinator
{
    bool IsTenantOffloaded(string tenantId, string topic, out TenantOffloadReason reason);

    void ForceOffload(string tenantId, string topic, TenantOffloadReason reason);

    void ClearOffload(string tenantId, string topic);

    ValueTask<EventHandlingResult> HandleOffloadAsync(TenantOffloadWorkItem workItem, CancellationToken cancellationToken);

    void RecordExecution(string tenantId, string topic, double durationMs);

    string ResolvePublishTopic(string pubSubName, string baseTopic, string? tenantId);

    ValueTask CleanupIdleResourcesAsync(CancellationToken cancellationToken);
}
