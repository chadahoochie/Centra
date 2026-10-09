using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Centra.Components;
using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.Topology;
using Centra.Sample.ControlPlane.Domain;
using Centra.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.ControlPlane.Services;

public sealed class SimulatedNodeFleetHostedService : BackgroundService
{
    private readonly ITopologyTracker _topologyTracker;
    private readonly IComponentCatalog? _componentCatalog;
    private readonly IResiliencePolicyCatalog? _resiliencePolicyCatalog;
    private readonly ILogger<SimulatedNodeFleetHostedService> _logger;

    public SimulatedNodeFleetHostedService(
        ITopologyTracker topologyTracker,
        ILogger<SimulatedNodeFleetHostedService> logger,
        IComponentCatalog? componentCatalog = null,
        IResiliencePolicyCatalog? resiliencePolicyCatalog = null)
    {
        _topologyTracker = topologyTracker ?? throw new ArgumentNullException(nameof(topologyTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _componentCatalog = componentCatalog;
        _resiliencePolicyCatalog = resiliencePolicyCatalog;
    }

    public static IReadOnlyList<SimulatedClusterNode> DefaultFleet { get; } =
    [
        // Cluster: cluster-alpha (E-Commerce)
        new("cluster-alpha", "order-service", "order-node-01", "alpha-secret-tok-901"),
        new("cluster-alpha", "order-service", "order-node-02", "alpha-secret-tok-901"),
        new("cluster-alpha", "inventory-service", "inv-node-01", "alpha-secret-tok-901"),
        new("cluster-alpha", "inventory-service", "inv-node-02", "alpha-secret-tok-901"),

        // Cluster: cluster-beta (Payments)
        new("cluster-beta", "payment-gateway", "pay-node-01", "beta-secret-tok-902"),
        new("cluster-beta", "payment-gateway", "pay-node-02", "beta-secret-tok-902"),
        new("cluster-beta", "settlement-worker", "settle-node-01", "beta-secret-tok-902"),

        // Cluster: cluster-gamma (Analytics / AI)
        new("cluster-gamma", "fraud-detector", "fraud-node-01", "gamma-secret-tok-903"),
        new("cluster-gamma", "fraud-detector", "fraud-node-02", "gamma-secret-tok-903"),
        new("cluster-gamma", "fraud-detector", "fraud-node-03", "gamma-secret-tok-903"),
        new("cluster-gamma", "feature-store", "feat-node-01", "gamma-secret-tok-903")
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Simulated Multi-Cluster Node Fleet Heartbeat Generator (11 nodes)...");

        // Seed sample components and resilience policies if catalogs are provided
        if (_componentCatalog is not null)
        {
            await SeedCatalogAsync(stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var node in DefaultFleet)
            {
                var request = new HeartbeatRequest(node.AppId, node.InstanceId, node.Status, node.Metadata, node.ClusterId);
                await _topologyTracker.RecordHeartbeatAsync(request, stoppingToken).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task SeedCatalogAsync(CancellationToken cancellationToken)
    {
        if (_componentCatalog is not null)
        {
            await _componentCatalog.UpsertComponentAsync(new ComponentDefinition
            {
                Name = "orders-cache",
                Type = ComponentType.StateStore,
                Provider = "redis",
                Metadata = new Dictionary<string, string> { ["host"] = "redis:6379" }
            }, cancellationToken).ConfigureAwait(false);

            await _componentCatalog.UpsertComponentAsync(new ComponentDefinition
            {
                Name = "billing-events",
                Type = ComponentType.PubSub,
                Provider = "rabbitmq",
                Metadata = new Dictionary<string, string> { ["host"] = "rabbitmq:5672" }
            }, cancellationToken).ConfigureAwait(false);
        }

        if (_resiliencePolicyCatalog is not null)
        {
            await _resiliencePolicyCatalog.UpsertPolicyAsync(new ResiliencePolicyDto
            {
                PolicyName = "payment-circuit-breaker",
                MaxRetries = 3,
                BaseDelayMs = 250,
                TimeoutSeconds = 2.0,
                BreakDurationSeconds = 5.0,
                FailureRatio = 0.5
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}
