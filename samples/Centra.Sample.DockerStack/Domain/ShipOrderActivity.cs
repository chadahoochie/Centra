using Centra.Workflows;

namespace Centra.Sample.DockerStack.Domain;

public sealed class ShipOrderActivity : WorkflowActivity<OrderProcessingRequest, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, OrderProcessingRequest input)
    {
        var trackingNumber = $"TRK-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        return ValueTask.FromResult(trackingNumber);
    }
}
