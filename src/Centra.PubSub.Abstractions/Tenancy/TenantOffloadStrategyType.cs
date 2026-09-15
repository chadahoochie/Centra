namespace Centra.PubSub.Tenancy;

public enum TenantOffloadStrategyType
{
    InProcessFairScheduler,
    EphemeralBrokerTopic,
    BoundedShardBrokerTopic
}
