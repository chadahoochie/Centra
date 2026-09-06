using Centra.Events;
using Centra.PubSub;
using Centra.Sample.OrdersService.Domain;
using Centra.State;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.OrdersService.Handlers;

[Topic("pubsub", "orders.created")]
public sealed class PaymentNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IStateStore<Order> _stateStore;
    private readonly ILogger<PaymentNotificationHandler> _logger;

    public PaymentNotificationHandler(
        IStateStore<Order> stateStore,
        ILogger<PaymentNotificationHandler> logger)
    {
        _stateStore = stateStore;
        _logger = logger;
    }

    public async Task<EventHandlingResult> HandleAsync(
        OrderCreatedEvent @event,
        EventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing payment notification for Order {OrderId} with Total {Total}", @event.OrderId, @event.TotalAmount);

        var existing = await _stateStore.GetAsync(@event.OrderId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existing.HasValue)
        {
            var updated = existing.Value.Value with { Status = "ProcessingPayment" };
            await _stateStore.TrySetAsync(@event.OrderId, updated, existing.Value.ETag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return EventHandlingResult.Success;
    }
}
