using Centra.Events;
using Centra.PubSub;
using Centra.Sample.OrdersService.Domain;
using Centra.State;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.OrdersService.Handlers;

[Topic("orders-pubsub", "orders.created", RuleFilter = "data.totalAmount >= 500", Priority = 10)]
public sealed class HighValueOrderNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IStateStore<Order> _stateStore;
    private readonly ILogger<HighValueOrderNotificationHandler> _logger;

    public HighValueOrderNotificationHandler(
        IStateStore<Order> stateStore,
        ILogger<HighValueOrderNotificationHandler> logger)
    {
        _stateStore = stateStore;
        _logger = logger;
    }

    public async Task<EventHandlingResult> HandleAsync(
        OrderCreatedEvent @event,
        EventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing expedited VIP payment for High-Value Order {OrderId} with Total {Total}",
            @event.OrderId,
            @event.TotalAmount);

        var existing = await _stateStore.GetAsync(@event.OrderId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existing.HasValue)
        {
            var updated = existing.Value.Value with { Status = "ProcessingVipPayment" };
            await _stateStore.TrySetAsync(@event.OrderId, updated, existing.Value.ETag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return EventHandlingResult.Success;
    }
}
