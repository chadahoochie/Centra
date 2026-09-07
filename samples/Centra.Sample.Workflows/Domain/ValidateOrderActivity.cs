using Centra.Workflows;

namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Activity that validates order request integrity.
/// </summary>
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
