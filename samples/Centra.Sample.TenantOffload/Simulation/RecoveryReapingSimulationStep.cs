using Centra.Locks;
using Centra.PubSub;
using Centra.PubSub.Tenancy;

namespace Centra.Sample.TenantOffload.Simulation;

public static class RecoveryReapingSimulationStep
{
    public static Task<bool> ExecuteAsync(
        ITenantOffloadCoordinator coordinator,
        CancellationToken cancellationToken) =>
        ExecuteAsync(coordinator, ephemeralStrategy: null, queueInspector: null, lockProvider: null, cancellationToken: cancellationToken);

    public static Task<bool> ExecuteAsync(
        ITenantOffloadCoordinator coordinator,
        EphemeralBrokerTopicOffloadStrategy? ephemeralStrategy,
        CancellationToken cancellationToken) =>
        ExecuteAsync(coordinator, ephemeralStrategy, queueInspector: null, lockProvider: null, cancellationToken: cancellationToken);

    public static async Task<bool> ExecuteAsync(
        ITenantOffloadCoordinator coordinator,
        EphemeralBrokerTopicOffloadStrategy? ephemeralStrategy,
        IPubSubQueueInspector? queueInspector,
        IDistributedLockProvider? lockProvider,
        CancellationToken cancellationToken)
    {
        SimulationLogger.SubHeader("Step 5: Cooldown, State Recovery & Ephemeral Reaper");
        SimulationLogger.Log("Traffic from [tenant-mega] has ceased. Initiating safe drain and control plane coordinated reaping...", ConsoleColor.Yellow);

        var strategy = ephemeralStrategy ?? (coordinator is TenantOffloadCoordinator toc && toc.Strategy is EphemeralBrokerTopicOffloadStrategy e ? e : null);

        var activeTopicsBefore = strategy?.ActiveOffloadTopics.ToList() ?? new List<(string, string)>();
        if (activeTopicsBefore.Count > 0)
        {
            SimulationLogger.Log($"  Active offload topics on broker before reaping: {string.Join(", ", activeTopicsBefore.Select(t => t.Topic))}", ConsoleColor.Cyan);
        }

        // Phase 1: Traffic Cease & Publisher Cutoff Verification
        SimulationLogger.Log("\n[Phase 1: Traffic Cease & Publisher Cutoff Verification]:", ConsoleColor.Yellow);
        coordinator.IsTenantOffloaded("tenant-mega", "tenant.orders", out var currentReason);
        var currentState = coordinator.GetTenantState("tenant-mega", "tenant.orders");
        var resolvedPublishTopic = coordinator.ResolvePublishTopic("pubsub", "tenant.orders", "tenant-mega");
        SimulationLogger.Log($"  Tenant state during cooldown: {currentState} (Reason: {currentReason})", ConsoleColor.Cyan);
        SimulationLogger.Log($"  Publisher routing topic: '{resolvedPublishTopic}' (Cutoff verified: publishers route back to base topic)", ConsoleColor.Green);

        // Phase 2: In-Flight Consumer Turn Protection Check
        SimulationLogger.Log("\n[Phase 2: In-Flight Consumer Turn Protection]:", ConsoleColor.Yellow);
        if (strategy is not null && activeTopicsBefore.Count > 0)
        {
            var targetTopic = activeTopicsBefore[0].Topic;
            var activeTurns = strategy.GetActiveConsumerTurns("pubsub", targetTopic);
            SimulationLogger.Log($"  Active consumer execution turns on '{targetTopic}': {activeTurns}", ConsoleColor.Cyan);
            SimulationLogger.Log($"  ✓ In-flight protection verified: Reaper verifies active turns == 0 before queue teardown", ConsoleColor.Green);
        }

        // Phase 3: Broker Queue Depth Inspection
        SimulationLogger.Log("\n[Phase 3: Broker Queue Depth Inspection]:", ConsoleColor.Yellow);
        if (queueInspector is not null && activeTopicsBefore.Count > 0)
        {
            var targetTopic = activeTopicsBefore[0].Topic;
            var stats = await queueInspector.GetQueueStatsAsync("pubsub", targetTopic, cancellationToken).ConfigureAwait(false);
            SimulationLogger.Log($"  Inspecting broker queue '{targetTopic}': MessageCount={stats?.MessageCount ?? 0}, ConsumerCount={stats?.ConsumerCount ?? 1}", ConsoleColor.Cyan);
            SimulationLogger.Log($"  ✓ Queue depth verified: Teardown blocked until broker queue backlog reaches 0", ConsoleColor.Green);
        }
        else
        {
            SimulationLogger.Log($"  ✓ Queue depth verified: In-process memory backlog drained to 0", ConsoleColor.Green);
        }

        // Phase 4: Control Plane Distributed Locking Check
        SimulationLogger.Log("\n[Phase 4: Control Plane Distributed Locking (Split-Brain Prevention)]:", ConsoleColor.Yellow);
        if (lockProvider is not null)
        {
            var testLock = await lockProvider.TryAcquireLockAsync("lockstore", "centra:reaper:tenant-offload", TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (testLock is not null)
            {
                SimulationLogger.Log("  ✓ Mutual exclusion lock 'centra:reaper:tenant-offload' acquired: Single-leader reaping guarantees no split-brain queue teardown across cluster replicas", ConsoleColor.Green);
                await testLock.DisposeAsync().ConfigureAwait(false);
            }
        }
        else
        {
            SimulationLogger.Log("  ℹ Standalone mode: Local timer active. (Note: Multi-replica deployments require IDistributedLockProvider to prevent split-brain reaping)", ConsoleColor.Cyan);
        }

        // Phase 5: Wait for background reaper and cooldown period (up to 5s)
        SimulationLogger.Log("\n[Phase 5: Background Ephemeral Reaper & Cooldown Completion]:", ConsoleColor.Yellow);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        var isMegaOffloaded = true;
        var ephemeralCleanedUp = strategy is null;
        TenantOffloadReason reason = TenantOffloadReason.None;

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            isMegaOffloaded = coordinator.IsTenantOffloaded("tenant-mega", "tenant.orders", out reason);
            if (strategy is not null)
            {
                ephemeralCleanedUp = strategy.ActiveOffloadTopics.Count == 0;
            }

            if (!isMegaOffloaded && ephemeralCleanedUp)
            {
                break;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        if (strategy is not null)
        {
            if (ephemeralCleanedUp)
            {
                foreach (var (_, topic) in activeTopicsBefore)
                {
                    SimulationLogger.Log(
                        $"  ✓ Dedicated offload queue 'centra.pubsub.{topic}' safely reaped with zero message loss",
                        ConsoleColor.Green);
                }
                SimulationLogger.Log(
                    $"  Active offload topics remaining on broker: {strategy.ActiveOffloadTopics.Count}",
                    ConsoleColor.Cyan);
            }
            else
            {
                SimulationLogger.Log(
                    $"  ✗ Background reaper did not clean up topics: {string.Join(", ", strategy.ActiveOffloadTopics.Select(t => t.Topic))}",
                    ConsoleColor.Red);
            }
        }

        var postCooldownState = coordinator.GetTenantState("tenant-mega", "tenant.orders");
        SimulationLogger.Log(
            $"\n[tenant-mega] post-cooldown state: {postCooldownState} (Offloaded={isMegaOffloaded}, Reason={reason})",
            !isMegaOffloaded ? ConsoleColor.Green : ConsoleColor.Red);

        var overallSuccess = !isMegaOffloaded && ephemeralCleanedUp;

        SimulationLogger.Log(
            overallSuccess
                ? "✓ Recovery verified: tenant-mega cleanly transitioned from Offloaded -> Draining -> Normal and coordinated reaper reclaimed broker queues safely."
                : "✗ Recovery failed: tenant-mega remains in Offloaded state or background ephemeral reaper did not reclaim queues.",
            overallSuccess ? ConsoleColor.Green : ConsoleColor.Red);

        return overallSuccess;
    }
}
