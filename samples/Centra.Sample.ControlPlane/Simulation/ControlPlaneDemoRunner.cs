using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.Dashboard;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.ControlPlane.HA;
using Centra.ControlPlane.Security;
using Centra.ControlPlane.Topology;
using Centra.Sample.ControlPlane.Domain;
using Centra.Sample.ControlPlane.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.ControlPlane.Simulation;

public static class ControlPlaneDemoRunner
{
    public const string AdminToken = "master-admin-key-777";
    public const string ClusterAlphaToken = "alpha-secret-tok-901";
    public const string ClusterBetaToken = "beta-secret-tok-902";
    public const string ClusterGammaToken = "gamma-secret-tok-903";

    public static async Task<ControlPlaneSimulationResult> RunAsync(
        string[]? args = null,
        CancellationToken cancellationToken = default,
        int holdSeconds = 0)
    {
        if (holdSeconds <= 0 && args != null)
        {
            var holdArg = args.FirstOrDefault(static a => a.StartsWith("--hold", StringComparison.OrdinalIgnoreCase));
            if (holdArg != null)
            {
                var parts = holdArg.Split('=');
                if (parts.Length == 2 && int.TryParse(parts[1], out var parsedHold))
                {
                    holdSeconds = parsedHold;
                }
                else
                {
                    var idx = Array.IndexOf(args, holdArg);
                    if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out var nextHold))
                    {
                        holdSeconds = nextHold;
                    }
                }
            }
        }

        SimulationLogger.Header("CENTRA FRAMEWORK - CONTROL PLANE EXPANSION & DASHBOARD SIMULATION");

        var stopwatch = Stopwatch.StartNew();
        var notes = new List<string>();

        // 1. Configure Security Options
        Action<ControlPlaneSecurityOptions> configureSecurity = options =>
        {
            options.Enabled = true;
            options.AdminToken = AdminToken;
            options.ClusterTokens["cluster-alpha"] = ClusterAlphaToken;
            options.ClusterTokens["cluster-beta"] = ClusterBetaToken;
            options.ClusterTokens["cluster-gamma"] = ClusterGammaToken;
        };

        // Shared cluster backing store (simulating Redis/PostgreSQL shared state in HA deployments)
        var sharedTopology = new InMemoryTopologyTracker();
        var sharedCatalog = new InMemoryComponentCatalog();
        var sharedResilienceCatalog = new InMemoryResiliencePolicyCatalog();

        // 2. Start Primary Control Plane in TestServer (Leader)
        SimulationLogger.Info("Spinning up Primary Control Plane node (cp-primary:8080) [Role: Active Leader]...");
        var primaryBuilder = WebApplication.CreateBuilder();
        primaryBuilder.WebHost.UseTestServer();
        primaryBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        primaryBuilder.Services.AddSingleton<ITopologyTracker>(sharedTopology);
        primaryBuilder.Services.AddSingleton<IComponentCatalog>(sharedCatalog);
        primaryBuilder.Services.AddSingleton<IComponentCatalogReader>(sharedCatalog);
        primaryBuilder.Services.AddSingleton<IComponentCatalogWriter>(sharedCatalog);
        primaryBuilder.Services.AddSingleton<IResiliencePolicyCatalog>(sharedResilienceCatalog);
        primaryBuilder.Services.AddCentraControlPlane();
        primaryBuilder.Services.AddCentraControlPlaneSecurity(configureSecurity);
        primaryBuilder.Services.AddCentraControlPlaneLeadership(options =>
        {
            options.Enabled = true;
            options.PublicEndpoint = "http://cp-primary:8080";
        });

        await using var primaryApp = primaryBuilder.Build();
        primaryApp.MapCentraControlPlaneEndpoints();
        primaryApp.MapCentraDashboard(new ControlPlaneDashboardOptions
        {
            Enabled = true,
            Path = "/dashboard",
            Title = "Centra Control Plane (Primary Leader)"
        });
        await primaryApp.StartAsync(cancellationToken).ConfigureAwait(false);

        var primaryTracker = primaryApp.Services.GetRequiredService<IControlPlaneLeaderTracker>();
        primaryTracker.SetLeader(true, "http://cp-primary:8080");

        var primaryClient = primaryApp.GetTestServer().CreateClient();

        // 3. Start Standby Control Plane in TestServer (Hot Backup)
        SimulationLogger.Info("Spinning up Standby Control Plane node (cp-standby:8081) [Role: Standby Replica]...");
        var standbyBuilder = WebApplication.CreateBuilder();
        standbyBuilder.WebHost.UseTestServer();
        standbyBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        standbyBuilder.Services.AddSingleton<ITopologyTracker>(sharedTopology);
        standbyBuilder.Services.AddSingleton<IComponentCatalog>(sharedCatalog);
        standbyBuilder.Services.AddSingleton<IComponentCatalogReader>(sharedCatalog);
        standbyBuilder.Services.AddSingleton<IComponentCatalogWriter>(sharedCatalog);
        standbyBuilder.Services.AddSingleton<IResiliencePolicyCatalog>(sharedResilienceCatalog);
        standbyBuilder.Services.AddCentraControlPlane();
        standbyBuilder.Services.AddCentraControlPlaneSecurity(configureSecurity);
        standbyBuilder.Services.AddCentraControlPlaneLeadership(options =>
        {
            options.Enabled = true;
            options.PublicEndpoint = "http://cp-standby:8081";
        });

        await using var standbyApp = standbyBuilder.Build();
        standbyApp.MapCentraControlPlaneEndpoints();
        standbyApp.MapCentraDashboard(new ControlPlaneDashboardOptions
        {
            Enabled = true,
            Path = "/dashboard",
            Title = "Centra Control Plane (Standby Replica)"
        });
        await standbyApp.StartAsync(cancellationToken).ConfigureAwait(false);

        var standbyTracker = standbyApp.Services.GetRequiredService<IControlPlaneLeaderTracker>();
        standbyTracker.SetLeader(false, "http://cp-primary:8080");

        var standbyRawClient = new HttpClient(new ControlPlaneSimulationHttpHandler(standbyApp.GetTestServer().CreateHandler()))
        {
            BaseAddress = new Uri("http://localhost/")
        };

        // --- STEP 1: HA Active-Passive Routing ---
        var step1Success = await HaRoutingSimulationStep.ExecuteAsync(primaryClient, standbyRawClient, cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 1 HA Active-Passive Routing: {(step1Success ? "PASSED" : "FAILED")}");

        // --- STEP 2: Rogue Node Defense & Dynamic Admission ---
        var step2Success = await RogueNodeDefenseSimulationStep.ExecuteAsync(primaryClient, "cluster-alpha", ClusterAlphaToken, cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 2 Rogue Node Defense: {(step2Success ? "PASSED" : "FAILED")}");

        // --- STEP 3: Multi-Cluster Fleet Expansion (N Clusters) ---
        var step3Success = await MultiClusterExpansionSimulationStep.ExecuteAsync(primaryClient, SimulatedNodeFleetHostedService.DefaultFleet, AdminToken, cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 3 Multi-Cluster Expansion: {(step3Success ? "PASSED" : "FAILED")}");

        // --- STEP 4: Dynamic Hot-Reload & Scoped Configuration Sync ---
        var step4Success = await DynamicSyncSimulationStep.ExecuteAsync(primaryClient, AdminToken, cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 4 Dynamic Catalog Hot-Reload: {(step4Success ? "PASSED" : "FAILED")}");

        // --- STEP 5: Active-Passive Failover Simulation ---
        var step5Success = await HaFailoverSimulationStep.ExecuteAsync(
            primaryTracker,
            standbyTracker,
            standbyRawClient,
            "http://cp-primary:8080",
            "http://cp-standby:8081",
            AdminToken,
            cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 5 HA Leader Failover: {(step5Success ? "PASSED" : "FAILED")}");

        // --- STEP 6: Embedded Dashboard Verification ---
        var step6Success = await DashboardVerificationSimulationStep.ExecuteAsync(standbyRawClient, AdminToken, cancellationToken).ConfigureAwait(false);
        notes.Add($"Step 6 Embedded Dashboard Verification: {(step6Success ? "PASSED" : "FAILED")}");

        stopwatch.Stop();

        // Optional Live Browser Inspection if --hold is passed
        if (holdSeconds > 0)
        {
            await RunLiveHoldServerAsync(holdSeconds, cancellationToken).ConfigureAwait(false);
        }

        var result = new ControlPlaneSimulationResult(
            HaRoutingSuccess: step1Success,
            RogueNodeDefenseSuccess: step2Success,
            MultiClusterExpansionSuccess: step3Success,
            DynamicSyncSuccess: step4Success,
            HaFailoverSuccess: step5Success,
            DashboardVerificationSuccess: step6Success,
            TotalElapsedMs: stopwatch.ElapsedMilliseconds,
            SummaryNotes: notes);

        SimulationLogger.Header(
            result.AllStepsSucceeded
                ? $"✓ SIMULATION COMPLETED SUCCESSFULLY: ALL 6 STEPS PASSED IN {stopwatch.ElapsedMilliseconds}ms"
                : "✗ SIMULATION COMPLETED WITH FAILURES");

        foreach (var note in notes)
        {
            if (note.Contains("PASSED", StringComparison.OrdinalIgnoreCase))
            {
                SimulationLogger.Success(note);
            }
            else
            {
                SimulationLogger.Error(note);
            }
        }

        return result;
    }

    internal static async Task RunLiveHoldServerAsync(int seconds, CancellationToken cancellationToken)
    {
        SimulationLogger.Header($"LIVE DASHBOARD SERVER ACTIVE FOR {seconds} SECONDS");
        SimulationLogger.Info("Booting live Kestrel HTTP instance with simulated multi-cluster heartbeat fleet...");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddCentraControlPlane();
        builder.Services.AddCentraControlPlaneSecurity(options =>
        {
            options.Enabled = false; // Allow local browser viewing without security hurdles
        });
        builder.Services.AddHostedService<SimulatedNodeFleetHostedService>();

        var app = builder.Build();
        app.MapCentraControlPlaneEndpoints();
        app.MapCentraDashboard(new ControlPlaneDashboardOptions
        {
            Enabled = true,
            Path = "/dashboard",
            Title = "Centra Control Plane Live Simulation Dashboard"
        });
        app.MapGet("/", () => Results.Redirect("/dashboard"));

        var url = "http://localhost:5050";
        app.Urls.Add(url);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        SimulationLogger.Success($"Live Control Plane Dashboard running at: {url}/dashboard");
        SimulationLogger.Info($"Open your browser at {url}/dashboard to view live multi-cluster topology.");
        SimulationLogger.Info($"Holding server open for {seconds}s (Press Ctrl+C to cancel)...");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SimulationLogger.Info("Live hold interrupted by user cancellation.");
        }
        finally
        {
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
