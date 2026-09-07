namespace Centra.Workflows;

/// <summary>
/// Execution lifecycle status of a workflow instance.
/// </summary>
public enum WorkflowStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Terminated = 4,
    Suspended = 5
}
