using Centra.Workflows;

namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Activity that schedules fulfillment shipping for a completed order.
/// </summary>
public sealed class ShipOrderActivity : WorkflowActivity<OrderProcessingRequest, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, OrderProcessingRequest input)
    {
        var trackingNumber = $"TRK-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        return ValueTask.FromResult(trackingNumber);
    }
}
