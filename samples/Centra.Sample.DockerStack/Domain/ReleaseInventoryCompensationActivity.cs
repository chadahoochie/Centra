using Centra.Workflows;

namespace Centra.Sample.DockerStack.Domain;

public sealed class ReleaseInventoryCompensationActivity : WorkflowActivity<InventoryReservation, bool>
{
    public override ValueTask<bool> RunAsync(WorkflowActivityContext context, InventoryReservation input)
    {
        return ValueTask.FromResult(true);
    }
}
