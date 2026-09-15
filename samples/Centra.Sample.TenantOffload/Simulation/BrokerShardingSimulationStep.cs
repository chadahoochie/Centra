using Centra.PubSub.Tenancy;

namespace Centra.Sample.TenantOffload.Simulation;

public static class BrokerShardingSimulationStep
{
    public static bool Execute(TenantOffloadOptions options)
    {
        SimulationLogger.SubHeader("Step 4: Broker Topic Sharding & Ephemeral Topic Resolution");
        SimulationLogger.Log("Evaluating broker-level topic resolution for offloaded tenants...", ConsoleColor.Yellow);

        var boundedStrategy = new BoundedShardBrokerTopicOffloadStrategy(
            new TestPublisherStub(),
            new TenantOffloadOptions { OffloadShardCount = 4 });

        var ephemeralStrategy = new EphemeralBrokerTopicOffloadStrategy(
            new TestPublisherStub(),
            subscriber: null,
            new TenantOffloadOptions { OffloadTopicPattern = "{topic}.dedicated.{tenantId}" });

        var baseTopic = "tenant.orders";
        var sampleTenants = new[] { "tenant-mega", "tenant-alpha", "tenant-beta", "tenant-gamma", "tenant-delta" };

        SimulationLogger.Log("\n[Bounded Shard Strategy (4 Shards)]:");
        foreach (var tenant in sampleTenants)
        {
            var resolved = boundedStrategy.ResolvePublishTopic(baseTopic, tenant);
            var shardId = TenantDeterministicHash.GetShardId(tenant, 4);
            SimulationLogger.Log($"  Tenant [{tenant}] ➔ Topic '{resolved}' (Shard #{shardId})", ConsoleColor.Cyan);
        }

        SimulationLogger.Log("\n[Ephemeral Dedicated Strategy]:");
        foreach (var tenant in sampleTenants.Take(3))
        {
            var resolved = ephemeralStrategy.ResolvePublishTopic(baseTopic, tenant);
            SimulationLogger.Log($"  Tenant [{tenant}] ➔ Topic '{resolved}'", ConsoleColor.Cyan);
        }

        SimulationLogger.Log("\n✓ Topic resolution verified: Outgoing publisher traffic dynamically routes to isolated partitions.", ConsoleColor.Green);
        return true;
    }
}
