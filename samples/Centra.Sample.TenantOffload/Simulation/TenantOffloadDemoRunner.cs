using System.Diagnostics;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Providers.RabbitMQ.Extensions;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Sample.TenantOffload.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Centra.Sample.TenantOffload.Simulation;

public static class TenantOffloadDemoRunner
{
    public static async Task<TenantOffloadSimulationResult> RunWithServicesAsync(
        IServiceProvider sp,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.Header("CENTRA DISTRIBUTED FRAMEWORK - DYNAMIC NOISY NEIGHBOR TENANT OFFLOADING SIMULATION");

        var stopwatch = Stopwatch.StartNew();
        var notes = new List<string>();

        var pubSub = sp.GetRequiredService<IPubSubClient>();
        var coordinator = sp.GetRequiredService<ITenantOffloadCoordinator>();
        var tracker = sp.GetRequiredService<ITenantMetricsTracker>();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TenantOffloadOptions>>().Value;

        var registry = sp.GetService<Centra.Registry.ComponentRegistry>();
        var centraOpts = sp.GetService<Microsoft.Extensions.Options.IOptions<Centra.Hosting.Options.CentraOptions>>()?.Value;
        var defaultPubSub = centraOpts?.DefaultPubSub ?? "pubsub";
        var driver = registry?.GetPubSubDriver(defaultPubSub) ?? sp.GetService<Centra.Drivers.IPubSubDriver>();
        var publisher = sp.GetService<IPubSubPublisher>() ?? (IPubSubPublisher?)driver;
        var subscriber = sp.GetService<IPubSubSubscriber>() ?? (IPubSubSubscriber?)driver;

        TenantOrderEventHandler.HandledCounts.Clear();

        // 1. Baseline Normal Traffic
        var step1Success = await NormalTrafficSimulationStep.ExecuteAsync(pubSub, coordinator, tracker, cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 1 Normal Traffic: {(step1Success ? "PASSED" : "FAILED")}");

        // 2 & 3. Noisy Neighbor Surge & Isolation
        var (step2Success, step3Success) = await NoisyNeighborSurgeSimulationStep.ExecuteAsync(pubSub, coordinator, tracker, cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 2 Noisy Neighbor Detection: {(step2Success ? "PASSED" : "FAILED")}");
        notes.Add($"Step 3 Fair Scheduling / Offload Isolation: {(step3Success ? "PASSED" : "FAILED")}");

        // 4. Broker Sharding & Ephemeral Topic Resolution (Spins up tenant queue on broker!)
        var step4Success = await BrokerShardingSimulationStep.ExecuteAsync(
            publisher ?? new TestPublisherStub(),
            subscriber,
            options,
            cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 4 Broker Topic Sharding: {(step4Success ? "PASSED" : "FAILED")}");

        // 5. Cooldown Recovery & Lane Reaping
        var step5Success = await RecoveryReapingSimulationStep.ExecuteAsync(coordinator, cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 5 Cooldown & Recovery: {(step5Success ? "PASSED" : "FAILED")}");

        // 6. Multi-Instance Distributed Consumption
        var step6Success = await MultiInstanceConsumptionSimulationStep.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 6 Multi-Instance Consumption: {(step6Success ? "PASSED" : "FAILED")}");

        stopwatch.Stop();

        var result = new TenantOffloadSimulationResult(
            BaselineNormalSuccess: step1Success,
            NoisyNeighborDetectedAndOffloaded: step2Success,
            FairSchedulingIsolationSuccess: step3Success,
            BrokerTopicShardingSuccess: step4Success,
            CooldownAndRecoverySuccess: step5Success,
            TotalElapsedMs: stopwatch.ElapsedMilliseconds,
            SummaryNotes: notes,
            MultiInstanceConsumptionSuccess: step6Success);

        SimulationLogger.Header(
            result.AllStepsSucceeded
                ? "✓ SIMULATION COMPLETED SUCCESSFULLY: ALL 6 STEPS PASSED"
                : "✗ SIMULATION COMPLETED WITH WARNINGS OR FAILURES");

        return result;
    }

    public static async Task<TenantOffloadSimulationResult> RunAsync(
        string[]? args = null,
        CancellationToken cancellationToken = default)
    {
        var rabbitConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__rabbitmq")
            ?? Environment.GetEnvironmentVariable("CENTRA_RABBITMQ_CONNECTIONSTRING");
        var rabbitHostName = Environment.GetEnvironmentVariable("RABBITMQ_HOSTNAME");

        var useRabbit = !string.IsNullOrWhiteSpace(rabbitConnectionString) ||
                        !string.IsNullOrWhiteSpace(rabbitHostName) ||
                        (args != null && args.Any(static a => a.Contains("rabbit", StringComparison.OrdinalIgnoreCase)));

        SimulationLogger.Log(
            useRabbit
                ? "Initializing host with RabbitMQ Pub/Sub and Dynamic Tenant Offload engine..."
                : "Initializing in-process host with Centra Pub/Sub and Tenant Offload engine...",
            ConsoleColor.Yellow);

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "tenant-offload-demo-app";
                    options.DefaultPubSub = "pubsub";
                }, typeof(TenantOrderEventHandler).Assembly);

                if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
                {
                    services.AddCentraRabbitMQPubSub("pubsub", options =>
                    {
                        options.ConnectionString = rabbitConnectionString;
                    });
                }
                else if (!string.IsNullOrWhiteSpace(rabbitHostName))
                {
                    services.AddCentraRabbitMQPubSub("pubsub", options =>
                    {
                        options.HostName = rabbitHostName;
                        options.UserName = "guest";
                        options.Password = "guest";
                    });
                }
                else
                {
                    services.AddCentraInMemory();
                }

                services.AddCentraTenantOffload(options =>
                {
                    options.WindowDuration = TimeSpan.FromSeconds(3);
                    options.MinSampleCount = 15;
                    options.TrafficShareThreshold = 0.55;
                    options.DurationMultiplierThreshold = 2.5;
                    options.CooldownPeriod = TimeSpan.FromSeconds(2);
                    options.OffloadStrategy = useRabbit
                        ? TenantOffloadStrategyType.EphemeralBrokerTopic
                        : TenantOffloadStrategyType.InProcessFairScheduler;
                    options.MaxConcurrencyPerTenant = 2;
                    options.PerTenantQueueCapacity = 200;
                    options.LaneIdleTimeout = TimeSpan.FromSeconds(1);
                    options.OffloadTopicPattern = "{topic}.offload.{tenantId}";
                });
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await RunWithServicesAsync(host.Services, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            host.Dispose();
        }
    }
}
