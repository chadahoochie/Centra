namespace Centra.Workflows;

/// <summary>
/// Internal control-flow exception thrown to suspend a workflow turn when waiting on timers or external events.
/// Orchestrations catching general exceptions must allow this exception to bubble up.
/// </summary>
public sealed class WorkflowSuspendedException : Exception
{
    public string Reason { get; }

    public WorkflowSuspendedException(string reason) : base($"Workflow suspended: {reason}")
    {
        Reason = reason;
    }
}
