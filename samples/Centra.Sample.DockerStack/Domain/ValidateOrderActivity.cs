using Centra.Workflows;

namespace Centra.Sample.DockerStack.Domain;

public sealed class ValidateOrderActivity : WorkflowActivity<OrderProcessingRequest, bool>
{
    public override ValueTask<bool> RunAsync(WorkflowActivityContext context, OrderProcessingRequest input)
    {
        if (input.Quantity <= 0 || input.TotalAmount <= 0 || string.IsNullOrWhiteSpace(input.ProductId))
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(true);
    }
}
