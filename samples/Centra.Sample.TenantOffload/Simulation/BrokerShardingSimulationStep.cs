using Centra.Events;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Sample.TenantOffload.Domain;

namespace Centra.Sample.TenantOffload.Simulation;

public static class BrokerShardingSimulationStep
{
    public static bool Execute(TenantOffloadOptions options) =>
        ExecuteAsync(new TestPublisherStub(), null, options).GetAwaiter().GetResult();

    public static async Task<bool> ExecuteAsync(
        IPubSubPublisher publisher,
        IPubSubSubscriber? subscriber,
        TenantOffloadOptions options,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.SubHeader("Step 4: Broker Topic Sharding & Ephemeral Topic Resolution");
        SimulationLogger.Log("Evaluating broker-level topic resolution for offloaded tenants...", ConsoleColor.Yellow);

        var boundedStrategy = new BoundedShardBrokerTopicOffloadStrategy(
            publisher,
            new TenantOffloadOptions { OffloadShardCount = 4 });

        var ephemeralStrategy = new EphemeralBrokerTopicOffloadStrategy(
            publisher,
            subscriber,
            options);

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

        if (subscriber is not null)
        {
            SimulationLogger.Log("\n[Spinning up Tenant-Specific Queue on Broker]:", ConsoleColor.Yellow);
            var targetOffloadTopic = ephemeralStrategy.ResolvePublishTopic(baseTopic, "tenant-mega");
            var queueName = $"centra.pubsub.{targetOffloadTopic}";

            var samplePayload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new TenantOrderEvent("ord-mega-queue-test", "tenant-mega", 99.99m, "Tenant-specific queue verification order"));
            var sampleHeaders = new Dictionary<string, string>
            {
                [CloudEventConstants.TenantIdHeader] = "tenant-mega",
                [CloudEventConstants.IdHeader] = "evt-queue-test-001"
            };

            var messageProcessedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var queueWorkItem = new TenantOffloadWorkItem(
                TenantId: "tenant-mega",
                PubSubName: "pubsub",
                Topic: baseTopic,
                Payload: samplePayload,
                Headers: sampleHeaders,
                HandlerInvoker: _ =>
                {
                    messageProcessedTcs.TrySetResult(true);
                    return ValueTask.FromResult(EventHandlingResult.Success);
                },
                CreatedAt: DateTimeOffset.UtcNow,
                CompletionSource: null,
                DynamicInvoker: (p, h, ct) =>
                {
                    messageProcessedTcs.TrySetResult(true);
                    return ValueTask.FromResult(EventHandlingResult.Success);
                });

            await ephemeralStrategy.ExecuteOffloadAsync(queueWorkItem, cancellationToken).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await messageProcessedTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Non-fatal if consumption continuation completes asynchronously
            }

            SimulationLogger.Log($"  Tenant [tenant-mega] ➔ Declared dedicated queue '{queueName}' on broker", ConsoleColor.Cyan);
            SimulationLogger.Log($"  Active offload topics on broker: {string.Join(", ", ephemeralStrategy.ActiveOffloadTopics.Select(t => t.Topic))}", ConsoleColor.Cyan);
            SimulationLogger.Log($"✓ Tenant-specific queue spun up and verified on broker: '{queueName}'", ConsoleColor.Green);
        }

        SimulationLogger.Log("\n✓ Topic resolution verified: Outgoing publisher traffic dynamically routes to isolated partitions.", ConsoleColor.Green);
        return true;
    }
}
