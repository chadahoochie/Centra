using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Defines a contract for processing execution turns of a workflow instance and applying lifecycle state transitions.
/// </summary>
public interface IWorkflowTurnProcessor
{
    /// <summary>
    /// Executes a single turn of the workflow instance, updating state and history accordingly.
    /// </summary>
    ValueTask ProcessTurnAsync(
        WorkflowInstanceId instanceId,
        WorkflowDefinition def,
        WorkflowStateRecord stateRecord,
        CancellationToken cancellationToken);
}
