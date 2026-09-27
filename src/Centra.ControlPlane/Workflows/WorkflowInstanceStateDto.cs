namespace Centra.ControlPlane.Workflows;

public sealed record WorkflowInstanceStateDto(
    string InstanceId,
    string WorkflowName,
    string Status,
    string? CustomStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    string? FailureDetails);
