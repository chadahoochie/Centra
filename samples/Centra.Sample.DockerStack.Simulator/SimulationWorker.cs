using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.DockerStack.Simulator;

/// <summary>
/// Continuously drives HTTP traffic at every capability the DockerStack sample exposes
/// (actors, pub/sub, state CAS, distributed locks, workflows, service invocation) so the
/// stack's Grafana dashboards always have live data to show, without any manual curl-ing.
/// </summary>
public sealed class SimulationWorker(
    SimulationOptions options,
    ILogger<SimulationWorker> logger) : BackgroundService
{
    public const string ActivitySourceName = "Centra.Sample.DockerStack.Simulator";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    // Weighted so actors/state/tasks (the cheap, high-frequency demos) dominate traffic, while
    // locks and workflows (which hold a lease or run a multi-step saga) fire less often.
    private static readonly (Operation Op, int Weight)[] Weights =
    [
        (Operation.ActorIncrement, 5),
        (Operation.StateCounterIncrement, 4),
        (Operation.TaskDispatch, 3),
        (Operation.PeerInfo, 3),
        (Operation.CronLastRun, 1),
        (Operation.LeaderLeaseCycle, 1),
        (Operation.WorkflowStart, 1),
    ];

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly int _totalWeight = Weights.Sum(w => w.Weight);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Simulator starting: nodes=[{Nodes}] interval={Interval} actorPoolSize={ActorPoolSize}",
            string.Join(", ", options.NodeBaseUrls), options.Interval, options.ActorPoolSize);

        // Give the nodes/control-plane time to come up and join the cluster before hammering them.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var node = PickNode();
            var operation = PickOperation();

            using var activity = ActivitySource.StartActivity($"simulate.{operation}");
            activity?.SetTag("simulator.node", node);

            try
            {
                await RunOperationAsync(operation, node, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Simulated {Operation} against {Node} failed", operation, node);
            }

            await Task.Delay(options.Interval, stoppingToken);
        }
    }

    private Task RunOperationAsync(Operation operation, string node, CancellationToken ct) => operation switch
    {
        Operation.ActorIncrement => IncrementActorAsync(node, ct),
        Operation.StateCounterIncrement => IncrementCounterAsync(node, ct),
        Operation.TaskDispatch => DispatchTaskAsync(node, ct),
        Operation.PeerInfo => GetPeerInfoAsync(node, ct),
        Operation.CronLastRun => GetCronLastRunAsync(node, ct),
        Operation.LeaderLeaseCycle => RunLeaderLeaseCycleAsync(node, ct),
        Operation.WorkflowStart => StartWorkflowAsync(node, ct),
        _ => Task.CompletedTask,
    };

    private async Task IncrementActorAsync(string node, CancellationToken ct)
    {
        var actorId = $"actor-{Random.Shared.Next(1, options.ActorPoolSize + 1)}";
        var response = await _http.PostAsync($"{node}/actors/{actorId}/increment", content: null, ct);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Incremented {ActorId} via {Node}", actorId, node);
    }

    private async Task IncrementCounterAsync(string node, CancellationToken ct)
    {
        // Occasionally force the ETag CAS retry path so the dashboard's conflict/retry panel stays alive.
        var simulateConflict = Random.Shared.Next(100) < 15;
        var response = await _http.PostAsJsonAsync(
            $"{node}/state/counter/increment",
            new { incrementBy = 1, simulateConflict },
            ct);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Incremented shared counter via {Node} (simulateConflict={SimulateConflict})", node, simulateConflict);
    }

    private async Task DispatchTaskAsync(string node, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            $"{node}/tasks/dispatch",
            new { taskType = "simulated", payload = $"generated-at-{DateTimeOffset.UtcNow:O}" },
            ct);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Dispatched task via {Node}", node);
    }

    private async Task GetPeerInfoAsync(string node, CancellationToken ct)
    {
        var response = await _http.GetAsync($"{node}/peers/info", ct);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Queried peer info via {Node}", node);
    }

    private async Task GetCronLastRunAsync(string node, CancellationToken ct)
    {
        var response = await _http.GetAsync($"{node}/cron/last-run", ct);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Queried cron last-run via {Node}", node);
    }

    private async Task RunLeaderLeaseCycleAsync(string node, CancellationToken ct)
    {
        var acquire = await _http.PostAsync($"{node}/leader/acquire?leaseSeconds=5", content: null, ct);
        if (acquire.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            logger.LogInformation("Leadership lease already held elsewhere (attempted via {Node})", node);
            return;
        }

        acquire.EnsureSuccessStatusCode();
        logger.LogInformation("Acquired leadership lease via {Node}", node);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        var release = await _http.PostAsync($"{node}/leader/release", content: null, ct);
        release.EnsureSuccessStatusCode();
        logger.LogInformation("Released leadership lease via {Node}", node);
    }

    private async Task StartWorkflowAsync(string node, CancellationToken ct)
    {
        // ~30% of orders exceed the $1000 threshold that ProcessPaymentActivity rejects, so the
        // saga's compensation path (ReleaseInventoryCompensationActivity) gets exercised too.
        var shouldFail = Random.Shared.Next(100) < 30;
        var totalAmount = shouldFail
            ? 1000m + Random.Shared.Next(1, 500)
            : Random.Shared.Next(10, 999);

        var orderId = $"sim-order-{Guid.NewGuid():N}"[..20];
        var request = new
        {
            orderId,
            customerId = $"sim-customer-{Random.Shared.Next(1, 50)}",
            productId = $"sim-sku-{Random.Shared.Next(1, 20)}",
            quantity = Random.Shared.Next(1, 5),
            totalAmount,
        };

        var response = await _http.PostAsJsonAsync($"{node}/centra/workflows/OrderProcessingWorkflow/start", request, ct);
        response.EnsureSuccessStatusCode();
        logger.LogInformation(
            "Started workflow {OrderId} via {Node} (totalAmount={TotalAmount}, expectFailure={ExpectFailure})",
            orderId, node, totalAmount, shouldFail);
    }

    private string PickNode() => options.NodeBaseUrls[Random.Shared.Next(options.NodeBaseUrls.Count)];

    private Operation PickOperation()
    {
        var roll = Random.Shared.Next(_totalWeight);
        var cumulative = 0;
        foreach (var (op, weight) in Weights)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                return op;
            }
        }

        return Weights[^1].Op;
    }

    public override void Dispose()
    {
        _http.Dispose();
        base.Dispose();
    }

    private enum Operation
    {
        ActorIncrement,
        StateCounterIncrement,
        TaskDispatch,
        PeerInfo,
        CronLastRun,
        LeaderLeaseCycle,
        WorkflowStart,
    }
}
