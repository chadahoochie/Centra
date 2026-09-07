namespace Centra.Workflows;

/// <summary>
/// Defines event types in a workflow's append-only execution history.
/// </summary>
public enum WorkflowHistoryEventType
{
    WorkflowStarted = 0,
    ActivityScheduled = 1,
    ActivityCompleted = 2,
    ActivityFailed = 3,
    TimerCreated = 4,
    TimerFired = 5,
    ExternalEventAwaited = 6,
    ExternalEventReceived = 7,
    SagaCompensationScheduled = 8,
    SagaCompensationStarted = 9,
    SagaCompensationCompleted = 10,
    WorkflowCompleted = 11,
    WorkflowFailed = 12,
    WorkflowTerminated = 13,
    WorkflowSuspended = 14
}
