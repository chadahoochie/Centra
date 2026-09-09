namespace Centra.Core.Workflows;

/// <summary>
/// Serializable record for persisting workflow instance state in a state store.
/// </summary>
public sealed class WorkflowStateRecord
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorkflowName { get; set; } = string.Empty;
    public int Status { get; set; }
    public byte[]? Input { get; set; }
    public byte[]? Output { get; set; }
    public string? CustomStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public string? FailureDetails { get; set; }
    public string? WaitingEventName { get; set; }
    public DateTimeOffset? TimerDueTime { get; set; }
}
