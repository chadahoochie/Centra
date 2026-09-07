using Centra.Workflows;

namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Orchestrates order processing including inventory reservation with LIFO saga compensation,
/// payment processing, a durable timer delay, and fulfillment shipping.
/// </summary>
public sealed class OrderProcessingWorkflow : Workflow<OrderProcessingRequest, OrderProcessingResult>
{
    public override async ValueTask<OrderProcessingResult> RunAsync(
        IWorkflowContext context,
        OrderProcessingRequest input)
    {
        context.SetCustomStatus("Validating");
        var isValid = await context.CallActivityAsync<ValidateOrderActivity, OrderProcessingRequest, bool>(input)
            .ConfigureAwait(false);

        if (!isValid)
        {
            context.SetCustomStatus("Rejected");
            return new OrderProcessingResult(
                input.OrderId,
                "Rejected",
                null,
                null,
                "Validation failed: Invalid quantity or total amount.");
        }

        var saga = context.CreateSaga();

        context.SetCustomStatus("ReservingInventory");
        var reservation = await context.CallActivityAsync<ReserveInventoryActivity, OrderProcessingRequest, InventoryReservation>(input)
            .ConfigureAwait(false);

        // Register compensation for inventory reservation
        saga.AddCompensation<ReleaseInventoryCompensationActivity, InventoryReservation>(reservation);

        try
        {
            context.SetCustomStatus("ProcessingPayment");
            var transactionId = await context.CallActivityAsync<ProcessPaymentActivity, OrderProcessingRequest, string>(input)
                .ConfigureAwait(false);

            context.SetCustomStatus("WaitingFulfillmentTimer");
            await context.CreateTimerAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);

            context.SetCustomStatus("Shipping");
            var trackingNumber = await context.CallActivityAsync<ShipOrderActivity, OrderProcessingRequest, string>(input)
                .ConfigureAwait(false);

            context.SetCustomStatus("Completed");
            return new OrderProcessingResult(
                input.OrderId,
                "Completed",
                transactionId,
                trackingNumber,
                "Order successfully fulfilled.");
        }
        catch (WorkflowSuspendedException)
        {
            // Allow control-flow suspension to propagate without compensation
            throw;
        }
        catch (Exception ex)
        {
            context.SetCustomStatus("Compensating");
            await saga.CompensateAsync().ConfigureAwait(false);
            context.SetCustomStatus("Failed");

            return new OrderProcessingResult(
                input.OrderId,
                "Failed",
                null,
                null,
                $"Saga compensated after error: {ex.Message}");
        }
    }
}
