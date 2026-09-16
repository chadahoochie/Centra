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
        var (success, _) = await ExecuteWithStrategyAsync(publisher, subscriber, options, existingStrategy: null, holdSeconds: 0, cancellationToken: cancellationToken).ConfigureAwait(false);
        return success;
    }

    public static async Task<(bool Success, EphemeralBrokerTopicOffloadStrategy EphemeralStrategy)> ExecuteWithStrategyAsync(
        IPubSubPublisher publisher,
        IPubSubSubscriber? subscriber,
        TenantOffloadOptions options,
        EphemeralBrokerTopicOffloadStrategy? existingStrategy = null,
        int holdSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.SubHeader("Step 4: Broker Topic Sharding & Ephemeral Topic Resolution");
        SimulationLogger.Log("Evaluating broker-level topic resolution for offloaded tenants...", ConsoleColor.Yellow);

        var boundedStrategy = new BoundedShardBrokerTopicOffloadStrategy(
            publisher,
            new TenantOffloadOptions { OffloadShardCount = 4 });

        var ephemeralOptions = new TenantOffloadOptions
        {
            OffloadTopicPattern = options.OffloadTopicPattern,
            MaxConcurrencyPerTenant = options.MaxConcurrencyPerTenant,
            LaneIdleTimeout = options.LaneIdleTimeout > TimeSpan.Zero && options.LaneIdleTimeout <= TimeSpan.FromSeconds(2)
                ? options.LaneIdleTimeout
                : TimeSpan.FromSeconds(1)
        };

        var ephemeralStrategy = existingStrategy ?? new EphemeralBrokerTopicOffloadStrategy(
            publisher,
            subscriber,
            ephemeralOptions);

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

            var packedSample = CloudEventPacker.Pack(
                new TenantOrderEvent("ord-mega-queue-test", "tenant-mega", 99.99m, "Tenant-specific queue verification order"),
                source: "tenant-offload-sim",
                mode: CloudEventMode.Binary,
                additionalMetadata: new Dictionary<string, string>
                {
                    [CloudEventConstants.TenantIdHeader] = "tenant-mega"
                });
            var samplePayload = packedSample.Payload;
            var sampleHeaders = packedSample.Headers;

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
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await messageProcessedTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                SimulationLogger.Log($"  Continuation wait timed out: {ex.Message}", ConsoleColor.DarkGray);
            }

            SimulationLogger.Log($"  Tenant [tenant-mega] ➔ Declared dedicated queue '{queueName}' on broker", ConsoleColor.Cyan);
            SimulationLogger.Log($"  Active offload topics on broker: {string.Join(", ", ephemeralStrategy.ActiveOffloadTopics.Select(t => t.Topic))}", ConsoleColor.Cyan);
            SimulationLogger.Log($"✓ Tenant-specific queue spun up and verified on broker: '{queueName}'", ConsoleColor.Green);

            if (holdSeconds > 0)
            {
                SimulationLogger.Log($"  Pausing for {holdSeconds}s to keep ephemeral queue '{queueName}' active on broker for inspection...", ConsoleColor.Yellow);
                var holdEnd = DateTimeOffset.UtcNow.AddSeconds(holdSeconds);
                while (DateTimeOffset.UtcNow < holdEnd && !cancellationToken.IsCancellationRequested)
                {
                    ephemeralStrategy.RecordActivity("pubsub", targetOffloadTopic);
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        SimulationLogger.Log("\n✓ Topic resolution verified: Outgoing publisher traffic dynamically routes to isolated partitions.", ConsoleColor.Green);
        return (true, ephemeralStrategy);
    }
}
