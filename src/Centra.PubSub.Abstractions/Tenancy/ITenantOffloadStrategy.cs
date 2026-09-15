using Centra.PubSub;

namespace Centra.PubSub.Tenancy;

public interface ITenantOffloadStrategy
{
    TenantOffloadStrategyType StrategyType { get; }

    ValueTask<EventHandlingResult> ExecuteOffloadAsync(TenantOffloadWorkItem workItem, CancellationToken cancellationToken);

    ValueTask CleanupIdleResourcesAsync(CancellationToken cancellationToken);

    string ResolvePublishTopic(string baseTopic, string tenantId);
}
