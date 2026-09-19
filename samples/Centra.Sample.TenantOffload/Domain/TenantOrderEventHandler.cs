using System.Collections.Concurrent;
using Centra.Events;
using Centra.PubSub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centra.Sample.TenantOffload.Domain;

[Topic("pubsub", "tenant.orders", EnableTenantOffload = true)]
public sealed class TenantOrderEventHandler : IEventHandler<TenantOrderEvent>
{
    public static readonly ConcurrentDictionary<string, int> HandledCounts = new();
    private readonly ITenantConsumerNodeState? _nodeState;
    private readonly ILogger<TenantOrderEventHandler> _logger;

    public TenantOrderEventHandler(
        ILogger<TenantOrderEventHandler>? logger = null,
        ITenantConsumerNodeState? nodeState = null)
    {
        _logger = logger ?? NullLogger<TenantOrderEventHandler>.Instance;
        _nodeState = nodeState;
    }

    public async Task<EventHandlingResult> HandleAsync(
        TenantOrderEvent @event,
        EventContext context,
        CancellationToken cancellationToken)
    {
        HandledCounts.AddOrUpdate(@event.TenantId, 1, static (_, count) => count + 1);

        var instanceId = _nodeState?.InstanceId ?? "in-process";
        var isOffloaded = context.Headers.ContainsKey("ce-offloaded") || context.Headers.ContainsKey("x-centra-offloaded");

        _nodeState?.RecordHandled(@event.TenantId, @event.OrderId, @event.Amount, isOffloaded);

        _logger.LogInformation(
            "[{InstanceId}] Processed order {OrderId} for tenant '{TenantId}' (Amount: {Amount:C})",
            instanceId,
            @event.OrderId,
            @event.TenantId,
            @event.Amount);

        // Small processing delay to simulate real business work
        await Task.Delay(5, cancellationToken).ConfigureAwait(false);

        return EventHandlingResult.Success;
    }
}
