using Centra.Events;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Sample.TenantOffload.Domain;

namespace Centra.Sample.TenantOffload.Simulation;

public static class NormalTrafficSimulationStep
{
    public static async Task<bool> ExecuteAsync(
        IPubSubClient pubSub,
        ITenantOffloadCoordinator coordinator,
        ITenantMetricsTracker tracker,
        CancellationToken cancellationToken)
    {
        SimulationLogger.SubHeader("Step 1: Baseline Multi-Tenant Traffic");
        SimulationLogger.Log("Simulating normal, fair traffic from three well-behaved tenants (alpha, beta, gamma)...");

        var tenants = new[] { "tenant-alpha", "tenant-beta", "tenant-gamma" };
        var messagesPerTenant = 10;

        for (var i = 1; i <= messagesPerTenant; i++)
        {
            foreach (var tenant in tenants)
            {
                var @event = new TenantOrderEvent(
                    OrderId: $"ord-{tenant}-{i}",
                    TenantId: tenant,
                    Amount: 100m + i,
                    Description: $"Standard order #{i} for {tenant}");

                var options = new PubSubPublishOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        [CloudEventConstants.TenantIdHeader] = tenant
                    }
                };

                await pubSub.PublishAsync("tenant.orders", @event, options, cancellationToken).ConfigureAwait(false);
            }
        }

        // Give broker time to dispatch across instances
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 3000 && !cancellationToken.IsCancellationRequested)
        {
            var allDone = true;
            foreach (var t in tenants)
            {
                if (tracker.GetTenantStats(t, "tenant.orders").MessageCount < messagesPerTenant)
                {
                    allDone = false;
                    break;
                }
            }
            if (allDone)
            {
                break;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        var allNormal = true;
        foreach (var tenant in tenants)
        {
            var stats = tracker.GetTenantStats(tenant, "tenant.orders");
            var isOffloaded = coordinator.IsTenantOffloaded(tenant, "tenant.orders", out var reason);

            SimulationLogger.Log(
                $"Tenant [{tenant}]: Messages={stats.MessageCount}, TrafficShare={stats.TrafficShareRatio:P1}, Offloaded={isOffloaded}, Reason={reason}",
                ConsoleColor.Green);

            if (isOffloaded)
            {
                allNormal = false;
            }
        }

        SimulationLogger.Log(
            allNormal
                ? "✓ Baseline verified: All tenants are in Normal state with balanced traffic shares."
                : "✗ Baseline failed: One or more tenants were prematurely offloaded.",
            allNormal ? ConsoleColor.Green : ConsoleColor.Red);

        return allNormal;
    }
}
