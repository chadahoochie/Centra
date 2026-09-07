using Centra.Workflows;

namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Compensating activity that releases reserved inventory when subsequent saga steps fail.
/// </summary>
public sealed class ReleaseInventoryCompensationActivity : WorkflowActivity<InventoryReservation, bool>
{
    public static int CompensationExecutions { get; set; }

    public override ValueTask<bool> RunAsync(WorkflowActivityContext context, InventoryReservation input)
    {
        CompensationExecutions++;
        return ValueTask.FromResult(true);
    }
}
