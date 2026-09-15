using System.Diagnostics;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Sample.TenantOffload.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Centra.Sample.TenantOffload.Simulation;

public static class TenantOffloadDemoRunner
{
    public static async Task<TenantOffloadSimulationResult> RunAsync(
        string[]? args = null,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.Header("CENTRA DISTRIBUTED FRAMEWORK - DYNAMIC NOISY NEIGHBOR TENANT OFFLOADING SIMULATION");

        var stopwatch = Stopwatch.StartNew();
        var notes = new List<string>();

        SimulationLogger.Log("Initializing in-process host with Centra Pub/Sub and Tenant Offload engine...", ConsoleColor.Yellow);

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "tenant-offload-demo-app";
                    options.DefaultPubSub = "pubsub";
                }, typeof(TenantOrderEventHandler).Assembly);

                services.AddCentraInMemory();

                services.AddCentraTenantOffload(options =>
                {
                    options.WindowDuration = TimeSpan.FromSeconds(3);
                    options.MinSampleCount = 15;
                    options.TrafficShareThreshold = 0.55;
                    options.DurationMultiplierThreshold = 2.5;
                    options.CooldownPeriod = TimeSpan.FromSeconds(2);
                    options.OffloadStrategy = TenantOffloadStrategyType.InProcessFairScheduler;
                    options.MaxConcurrencyPerTenant = 2;
                    options.PerTenantQueueCapacity = 200;
                    options.LaneIdleTimeout = TimeSpan.FromSeconds(1);
                });
            })
            .Build();

        TenantOrderEventHandler.HandledCounts.Clear();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var sp = host.Services;
            var pubSub = sp.GetRequiredService<IPubSubClient>();
            var coordinator = sp.GetRequiredService<ITenantOffloadCoordinator>();
            var tracker = sp.GetRequiredService<ITenantMetricsTracker>();
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TenantOffloadOptions>>().Value;

            // 1. Baseline Normal Traffic
            var step1Success = await NormalTrafficSimulationStep.ExecuteAsync(pubSub, coordinator, tracker, cancellationToken).ConfigureAwait(false);
            notes.Add($"Step 1 Normal Traffic: {(step1Success ? "PASSED" : "FAILED")}");

            // 2 & 3. Noisy Neighbor Surge & In-Process Fair Scheduling
            var (step2Success, step3Success) = await NoisyNeighborSurgeSimulationStep.ExecuteAsync(pubSub, coordinator, tracker, cancellationToken).ConfigureAwait(false);
            notes.Add($"Step 2 Noisy Neighbor Detection: {(step2Success ? "PASSED" : "FAILED")}");
            notes.Add($"Step 3 Fair Scheduling Isolation: {(step3Success ? "PASSED" : "FAILED")}");

            // 4. Broker Sharding & Ephemeral Topic Resolution
            var step4Success = BrokerShardingSimulationStep.Execute(options);
            notes.Add($"Step 4 Broker Topic Sharding: {(step4Success ? "PASSED" : "FAILED")}");

            // 5. Cooldown Recovery & Lane Reaping
            var step5Success = await RecoveryReapingSimulationStep.ExecuteAsync(coordinator, cancellationToken).ConfigureAwait(false);
            notes.Add($"Step 5 Cooldown & Recovery: {(step5Success ? "PASSED" : "FAILED")}");

            stopwatch.Stop();

            var result = new TenantOffloadSimulationResult(
                BaselineNormalSuccess: step1Success,
                NoisyNeighborDetectedAndOffloaded: step2Success,
                FairSchedulingIsolationSuccess: step3Success,
                BrokerTopicShardingSuccess: step4Success,
                CooldownAndRecoverySuccess: step5Success,
                TotalElapsedMs: stopwatch.ElapsedMilliseconds,
                SummaryNotes: notes);

            SimulationLogger.Header(
                result.AllStepsSucceeded
                    ? "✓ SIMULATION COMPLETED SUCCESSFULLY: ALL 5 STEPS PASSED"
                    : "✗ SIMULATION COMPLETED WITH WARNINGS OR FAILURES");

            return result;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
        }
    }
}
