namespace Centra.Workflows;

/// <summary>
/// Snapshot state of a workflow instance.
/// </summary>
public readonly record struct WorkflowState(
    WorkflowInstanceId InstanceId,
    string WorkflowName,
    WorkflowStatus Status,
    ReadOnlyMemory<byte> Input,
    ReadOnlyMemory<byte> Output,
    string? CustomStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    string? FailureDetails);
