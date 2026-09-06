using Centra.Events;
using Centra.PubSub;
using Centra.State;

namespace Centra.Tests.Integration.Workflows;

[Topic("pubsub", "orders.created")]
public sealed class IntegrationPaymentHandler : IEventHandler<IntegrationOrderCreatedEvent>
{
    private readonly IStateStore<IntegrationOrderState> _stateStore;

    public static string? LastReceivedCorrelationId { get; set; }
    public static bool WasExecuted { get; set; }

    public IntegrationPaymentHandler(IStateStore<IntegrationOrderState> stateStore)
    {
        _stateStore = stateStore;
    }

    public async Task<EventHandlingResult> HandleAsync(
        IntegrationOrderCreatedEvent @event,
        EventContext context,
        CancellationToken cancellationToken = default)
    {
        WasExecuted = true;
        LastReceivedCorrelationId = context.CorrelationId ?? CentraAmbientContext.CorrelationId;

        var existing = await _stateStore.GetAsync(@event.OrderId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existing.HasValue)
        {
            var updated = existing.Value.Value with { Status = "Paid" };
            await _stateStore.TrySetAsync(@event.OrderId, updated, existing.Value.ETag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return EventHandlingResult.Success;
    }
}
