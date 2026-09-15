using System.Collections.Concurrent;
using Centra.Events;
using Centra.PubSub;

namespace Centra.Sample.TenantOffload.Domain;

[Topic("pubsub", "tenant.orders", EnableTenantOffload = true)]
public sealed class TenantOrderEventHandler : IEventHandler<TenantOrderEvent>
{
    public static readonly ConcurrentDictionary<string, int> HandledCounts = new();

    public async Task<EventHandlingResult> HandleAsync(
        TenantOrderEvent @event,
        EventContext context,
        CancellationToken cancellationToken)
    {
        HandledCounts.AddOrUpdate(@event.TenantId, 1, (_, count) => count + 1);

        // Small processing delay to simulate real business work
        await Task.Delay(5, cancellationToken).ConfigureAwait(false);

        return EventHandlingResult.Success;
    }
}
