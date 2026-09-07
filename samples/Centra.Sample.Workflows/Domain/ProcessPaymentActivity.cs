using Centra.Workflows;

namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Activity that processes payment for an order. Fails if total amount exceeds limit, triggering saga rollback.
/// </summary>
public sealed class ProcessPaymentActivity : WorkflowActivity<OrderProcessingRequest, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, OrderProcessingRequest input)
    {
        if (input.TotalAmount > 1000.00m)
        {
            throw new InvalidOperationException(
                $"Payment declined: Amount ${input.TotalAmount:F2} exceeds credit limit ($1,000.00).");
        }

        var transactionId = $"txn-{Guid.NewGuid():N}"[..12];
        return ValueTask.FromResult(transactionId);
    }
}
