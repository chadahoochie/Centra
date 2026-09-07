namespace Centra.Workflows;

/// <summary>
/// Exception thrown when a workflow activity invocation fails.
/// </summary>
public sealed class WorkflowActivityExecutionException : Exception
{
    public string ActivityName { get; }

    public WorkflowActivityExecutionException(string activityName, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ActivityName = activityName;
    }
}
