namespace Centra.Core.Workflows;

/// <summary>
/// Serializable record containing the complete history event stream for a workflow instance.
/// </summary>
public sealed class WorkflowHistoryRecord
{
    public List<WorkflowHistoryEventRecord> Events { get; set; } = [];
}
