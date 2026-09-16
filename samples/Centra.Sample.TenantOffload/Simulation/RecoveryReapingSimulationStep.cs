using Centra.PubSub.Tenancy;

namespace Centra.Sample.TenantOffload.Simulation;

public static class RecoveryReapingSimulationStep
{
    public static Task<bool> ExecuteAsync(
        ITenantOffloadCoordinator coordinator,
        CancellationToken cancellationToken) =>
        ExecuteAsync(coordinator, ephemeralStrategy: null, cancellationToken);

    public static async Task<bool> ExecuteAsync(
        ITenantOffloadCoordinator coordinator,
        EphemeralBrokerTopicOffloadStrategy? ephemeralStrategy,
        CancellationToken cancellationToken)
    {
        SimulationLogger.SubHeader("Step 5: Cooldown, State Recovery & Ephemeral Reaper");
        SimulationLogger.Log("Traffic from [tenant-mega] has ceased. Waiting for cooldown and background ephemeral reaper (TenantOffloadReaperHostedService)...", ConsoleColor.Yellow);

        var strategy = ephemeralStrategy ?? (coordinator is TenantOffloadCoordinator toc && toc.Strategy is EphemeralBrokerTopicOffloadStrategy e ? e : null);

        var activeTopicsBefore = strategy?.ActiveOffloadTopics.ToList() ?? new List<(string, string)>();
        if (activeTopicsBefore.Count > 0)
        {
            SimulationLogger.Log($"  Active offload topics on broker before reaping: {string.Join(", ", activeTopicsBefore.Select(t => t.Topic))}", ConsoleColor.Cyan);
        }

        // Wait for background reaper and cooldown period (up to 5s)
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
            SimulationLogger.Log("\n[Background Ephemeral Reaper Verification]:", ConsoleColor.Yellow);
            if (ephemeralCleanedUp)
            {
                foreach (var (_, topic) in activeTopicsBefore)
                {
                    SimulationLogger.Log(
                        $"  ✓ Dedicated offload queue 'centra.pubsub.{topic}' automatically reaped in background by TenantOffloadReaperHostedService (AutoDelete=true)",
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

        SimulationLogger.Log(
            $"\n[tenant-mega] post-cooldown state: Offloaded={isMegaOffloaded}, Reason={reason}",
            !isMegaOffloaded ? ConsoleColor.Green : ConsoleColor.Red);

        var overallSuccess = !isMegaOffloaded && ephemeralCleanedUp;

        SimulationLogger.Log(
            overallSuccess
                ? "✓ Recovery verified: tenant-mega cleanly transitioned from Offloaded back to Normal and background ephemeral reaper reclaimed broker queues."
                : "✗ Recovery failed: tenant-mega remains in Offloaded state or background ephemeral reaper did not reclaim queues.",
            overallSuccess ? ConsoleColor.Green : ConsoleColor.Red);

        return overallSuccess;
    }
}
