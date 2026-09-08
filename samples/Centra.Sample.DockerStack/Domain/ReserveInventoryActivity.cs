using Centra.Workflows;

namespace Centra.Sample.DockerStack.Domain;

public sealed class ReserveInventoryActivity : WorkflowActivity<OrderProcessingRequest, InventoryReservation>
{
    public override ValueTask<InventoryReservation> RunAsync(WorkflowActivityContext context, OrderProcessingRequest input)
    {
        var reservationId = $"res-{Guid.NewGuid():N}"[..12];
        return ValueTask.FromResult(new InventoryReservation(reservationId, input.ProductId, input.Quantity));
    }
}
