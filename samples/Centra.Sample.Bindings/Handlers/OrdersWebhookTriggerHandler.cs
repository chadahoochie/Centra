using System.Text;
using System.Text.Json;
using Centra.Bindings;
using Centra.State;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.Bindings.Handlers;

public sealed class OrdersWebhookTriggerHandler : IBindingTriggerHandler
{
    private readonly IStateStore<OrderState> _stateStore;
    private readonly ILogger<OrdersWebhookTriggerHandler> _logger;

    public static int Invocations;

    public OrdersWebhookTriggerHandler(
        IStateStore<OrderState> stateStore,
        ILogger<OrdersWebhookTriggerHandler> logger)
    {
        _stateStore = stateStore;
        _logger = logger;
    }

    public async ValueTask<BindingResponse> HandleTriggerAsync(
        BindingData data,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Invocations);

        var rawJson = Encoding.UTF8.GetString(data.Data.Span);
        var order = JsonSerializer.Deserialize<InboundOrder>(rawJson, JsonSerializerOptions.Web);

        if (order is null)
        {
            var errBytes = Encoding.UTF8.GetBytes("Invalid JSON");
            return new BindingResponse(errBytes, new Dictionary<string, string> { ["status"] = "error" });
        }

        var orderState = new OrderState(order.OrderId, order.Amount, "ProcessedViaWebhook");
        await _stateStore.SetAsync($"order:{order.OrderId}", orderState, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[INPUT BINDING] Webhook processed order '{OrderId}' for ${Amount}. Persisted in state store.",
            order.OrderId,
            order.Amount);

        var responsePayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { status = "acknowledged", orderId = order.OrderId }));
        var metadata = new Dictionary<string, string>
        {
            ["content-type"] = "application/json",
            ["header:X-Centra-Processed"] = "true"
        };

        return new BindingResponse(responsePayload, metadata);
    }
}
