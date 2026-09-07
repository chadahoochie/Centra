namespace Centra.Sample.Workflows.Simulation;

/// <summary>
/// Execution metrics and verification flags for the workflow simulation runner.
/// </summary>
public sealed record WorkflowSimulationResult(
    bool HappyPathCompleted,
    bool SagaRollbackSucceeded,
    bool ExternalApprovalSucceeded,
    string HappyPathOrderId,
    string SagaOrderId,
    string ApprovalRequestId,
    IReadOnlyList<string> Logs);
