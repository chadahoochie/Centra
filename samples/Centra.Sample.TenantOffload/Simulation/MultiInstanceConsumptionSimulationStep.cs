using Centra.Events;
using Centra.Sample.TenantOffload.Domain;

namespace Centra.Sample.TenantOffload.Simulation;

public static class MultiInstanceConsumptionSimulationStep
{
    public static async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        SimulationLogger.SubHeader("Step 6: Multi-Instance Distributed Consumption");
        SimulationLogger.Log("Simulating multi-replica consumer nodes (replica-1, replica-2) consuming tenant events...");

        var node1 = new TenantConsumerNodeState("replica-1", "RabbitMQ");
        var node2 = new TenantConsumerNodeState("replica-2", "RabbitMQ");

        var handler1 = new TenantOrderEventHandler(null, node1);
        var handler2 = new TenantOrderEventHandler(null, node2);

        var eventContext = new EventContext(
            Id: "evt-multi-001",
            Topic: "tenant.orders",
            PubSubName: "pubsub",
            Source: "centra://sample/multi-instance",
            Type: "tenant.order.created",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: null,
            CausationId: null,
            TenantId: null,
            Headers: new Dictionary<string, string>());

        for (var i = 1; i <= 20; i++)
        {
            var tenant = (i % 4 == 0) ? "tenant-mega" : "tenant-alpha";
            var order = new TenantOrderEvent($"ord-multi-{i}", tenant, 50m + i, $"Multi-instance order #{i}");

            if (i % 2 == 1)
            {
                await handler1.HandleAsync(order, eventContext, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await handler2.HandleAsync(order, eventContext, cancellationToken).ConfigureAwait(false);
            }
        }

        SimulationLogger.Log($"[replica-1] Total Handled: {node1.TotalHandled}, Tenant Counts: {string.Join(", ", node1.TenantHandledCounts.Select(static kvp => $"{kvp.Key}={kvp.Value}"))}", ConsoleColor.Cyan);
        SimulationLogger.Log($"[replica-2] Total Handled: {node2.TotalHandled}, Tenant Counts: {string.Join(", ", node2.TenantHandledCounts.Select(static kvp => $"{kvp.Key}={kvp.Value}"))}", ConsoleColor.Cyan);

        var success = node1.TotalHandled == 10 && node2.TotalHandled == 10 &&
                      node1.TenantHandledCounts.ContainsKey("tenant-alpha") &&
                      node2.TenantHandledCounts.ContainsKey("tenant-alpha");

        SimulationLogger.Log(
            success
                ? "✓ Multi-instance consumption verified: Traffic was cleanly distributed across consumer replicas."
                : "✗ Multi-instance consumption failed.",
            success ? ConsoleColor.Green : ConsoleColor.Red);

        return success;
    }
}
