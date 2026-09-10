using Centra.Events;
using Centra.PubSub;
using Centra.Sample.RabbitSimulation.Contracts.Clients;
using Centra.Sample.RabbitSimulation.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.RabbitSimulation.Consumer.Handlers;

[Topic("pubsub", "orders.new")]
public sealed class OrderSubmittedEventHandler : IEventHandler<OrderMessage>
{
    private readonly IOrderApiClient _apiClient;
    private readonly ILogger<OrderSubmittedEventHandler> _logger;

    public OrderSubmittedEventHandler(
        IOrderApiClient apiClient,
        ILogger<OrderSubmittedEventHandler> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EventHandlingResult> HandleAsync(
        OrderMessage @event,
        EventContext context,
        CancellationToken cancellationToken = default)
    {
        var totalAmount = @event.Quantity * @event.UnitPrice;

        _logger.LogInformation(
            "[Consumer] Received Order {OrderId} from RabbitMQ pub/sub (CloudEvent ID: {EventId}, CorrelationId: {CorrelationId}). Customer: '{CustomerName}', Item: '{ItemDescription}', Total: ${Total:F2}",
            @event.OrderId,
            context.Id,
            context.CorrelationId ?? "N/A",
            @event.CustomerName,
            @event.ItemDescription,
            totalAmount);

        var request = new OrderProcessRequest(
            OrderId: @event.OrderId,
            CustomerName: @event.CustomerName,
            ItemDescription: @event.ItemDescription,
            Quantity: @event.Quantity,
            UnitPrice: @event.UnitPrice,
            TotalAmount: totalAmount,
            SubmittedAt: @event.CreatedAt);

        try
        {
            _logger.LogInformation(
                "[Consumer] Invoking 'rabbit-api' service via Centra Service Invocation for Order {OrderId}...",
                @event.OrderId);

            var response = await _apiClient.ProcessOrderAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[Consumer] Centra Service Invocation SUCCESS for Order {OrderId}! Status: {Status}, AuthCode: {AuthCode}, FinalAmount: ${FinalAmount:F2}, Message: {Message}",
                response.OrderId,
                response.Status,
                response.AuthorizationCode,
                response.FinalAmount,
                response.Message);

            return EventHandlingResult.Success;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "[Consumer] Error during Centra Service Invocation for Order {OrderId}. Message will be retried.",
                @event.OrderId);

            return EventHandlingResult.Retry;
        }
    }
}
