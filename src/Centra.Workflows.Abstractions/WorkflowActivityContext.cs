namespace Centra.Workflows;

/// <summary>
/// Execution context provided to a running workflow activity.
/// </summary>
public sealed class WorkflowActivityContext
{
    public WorkflowInstanceId InstanceId { get; }
    public string ActivityName { get; }
    public CancellationToken CancellationToken { get; }

    public WorkflowActivityContext(WorkflowInstanceId instanceId, string activityName, CancellationToken cancellationToken = default)
    {
        InstanceId = instanceId;
        ActivityName = activityName;
        CancellationToken = cancellationToken;
    }
}
