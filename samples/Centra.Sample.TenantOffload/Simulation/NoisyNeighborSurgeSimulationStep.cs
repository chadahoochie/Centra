using Centra.Events;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Sample.TenantOffload.Domain;

namespace Centra.Sample.TenantOffload.Simulation;

public static class NoisyNeighborSurgeSimulationStep
{
    public static async Task<(bool OffloadedDetected, bool FairSchedulingIsolated)> ExecuteAsync(
        IPubSubClient pubSub,
        ITenantOffloadCoordinator coordinator,
        ITenantMetricsTracker tracker,
        CancellationToken cancellationToken)
    {
        SimulationLogger.SubHeader("Step 2 & 3: Noisy Neighbor Surge & In-Process Fair Scheduling");
        SimulationLogger.Log("Tenant [tenant-mega] begins a high-frequency surge of 60 orders...", ConsoleColor.Magenta);
        SimulationLogger.Log("Tenant [tenant-alpha] concurrently submits 5 priority orders...", ConsoleColor.Cyan);

        var noisyTenant = "tenant-mega";
        var honestTenant = "tenant-alpha";

        // Surge from noisy tenant
        for (var i = 1; i <= 60; i++)
        {
            var @event = new TenantOrderEvent(
                OrderId: $"ord-{noisyTenant}-{i}",
                TenantId: noisyTenant,
                Amount: 9.99m,
                Description: $"High-frequency batch item #{i}");

            var options = new PubSubPublishOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    [CloudEventConstants.TenantIdHeader] = noisyTenant
                }
            };

            await pubSub.PublishAsync("tenant.orders", @event, options, cancellationToken).ConfigureAwait(false);
        }

        // Concurrent orders from honest tenant
        for (var i = 101; i <= 105; i++)
        {
            var @event = new TenantOrderEvent(
                OrderId: $"ord-{honestTenant}-{i}",
                TenantId: honestTenant,
                Amount: 500m,
                Description: $"High-priority order #{i}");

            var options = new PubSubPublishOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    [CloudEventConstants.TenantIdHeader] = honestTenant
                }
            };

            await pubSub.PublishAsync("tenant.orders", @event, options, cancellationToken).ConfigureAwait(false);
        }

        // Allow worker lanes and primary dispatcher to finish processing
        await Task.Delay(400, cancellationToken).ConfigureAwait(false);

        var statsMega = tracker.GetTenantStats(noisyTenant, "tenant.orders");
        var isMegaOffloaded = coordinator.IsTenantOffloaded(noisyTenant, "tenant.orders", out var megaReason);

        var statsAlpha = tracker.GetTenantStats(honestTenant, "tenant.orders");
        var isAlphaOffloaded = coordinator.IsTenantOffloaded(honestTenant, "tenant.orders", out _);

        SimulationLogger.Log(
            $"[tenant-mega]:  TrafficShare={statsMega.TrafficShareRatio:P1}, Offloaded={isMegaOffloaded}, Reason={megaReason}",
            isMegaOffloaded ? ConsoleColor.Yellow : ConsoleColor.Red);

        SimulationLogger.Log(
            $"[tenant-alpha]: TrafficShare={statsAlpha.TrafficShareRatio:P1}, Offloaded={isAlphaOffloaded}",
            !isAlphaOffloaded ? ConsoleColor.Green : ConsoleColor.Red);

        var offloadDetected = isMegaOffloaded && (megaReason & TenantOffloadReason.HighTrafficShare) != 0;
        var honestProtected = !isAlphaOffloaded;

        TenantOrderEventHandler.HandledCounts.TryGetValue(honestTenant, out var alphaHandled);
        TenantOrderEventHandler.HandledCounts.TryGetValue(noisyTenant, out var megaHandled);

        SimulationLogger.Log(
            $"Handled events: [tenant-alpha]={alphaHandled} (processed without delay), [tenant-mega]={megaHandled} (isolated in worker lane)",
            ConsoleColor.Cyan);

        var fairSchedulingSuccess = honestProtected && alphaHandled >= 15 && megaHandled >= 50;

        SimulationLogger.Log(
            offloadDetected
                ? "✓ Noisy neighbor detected: tenant-mega was successfully isolated into offload state."
                : "✗ Noisy neighbor detection failed.",
            offloadDetected ? ConsoleColor.Green : ConsoleColor.Red);

        SimulationLogger.Log(
            fairSchedulingSuccess
                ? "✓ Fair scheduling verified: Honest tenant processed concurrently while noisy tenant was isolated."
                : "✗ Fair scheduling failed.",
            fairSchedulingSuccess ? ConsoleColor.Green : ConsoleColor.Red);

        return (offloadDetected, fairSchedulingSuccess);
    }
}
