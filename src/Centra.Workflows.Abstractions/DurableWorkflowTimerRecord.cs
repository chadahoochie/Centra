namespace Centra.Workflows;

/// <summary>
/// Represents a durable timer schedule persisted across workflow engine restarts and cluster node failovers.
/// </summary>
public sealed record DurableWorkflowTimerRecord(
    WorkflowInstanceId InstanceId,
    int EventId,
    DateTimeOffset DueTimeUtc,
    DateTimeOffset CreatedAtUtc);
