using Centra.PubSub.Tenancy;

namespace Centra.Sample.TenantOffload.Simulation;

public static class RecoveryReapingSimulationStep
{
    public static async Task<bool> ExecuteAsync(
        ITenantOffloadCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        SimulationLogger.SubHeader("Step 5: Cooldown, State Recovery & Lane Reaping");
        SimulationLogger.Log("Traffic from [tenant-mega] has ceased. Waiting for cooldown period to expire...");

        // Cooldown period is 2s and WindowDuration is 3s; wait 3.2s for burst to slide out of evaluation window
        await Task.Delay(3200, cancellationToken).ConfigureAwait(false);

        // Run idle resource cleanup
        await coordinator.CleanupIdleResourcesAsync(cancellationToken).ConfigureAwait(false);

        var isMegaOffloaded = coordinator.IsTenantOffloaded("tenant-mega", "tenant.orders", out var reason);

        SimulationLogger.Log(
            $"[tenant-mega] post-cooldown state: Offloaded={isMegaOffloaded}, Reason={reason}",
            !isMegaOffloaded ? ConsoleColor.Green : ConsoleColor.Red);

        SimulationLogger.Log(
            !isMegaOffloaded
                ? "✓ Recovery verified: tenant-mega cleanly transitioned from Offloaded back to Normal."
                : "✗ Recovery failed: tenant-mega remains in Offloaded state.",
            !isMegaOffloaded ? ConsoleColor.Green : ConsoleColor.Red);

        return !isMegaOffloaded;
    }
}
