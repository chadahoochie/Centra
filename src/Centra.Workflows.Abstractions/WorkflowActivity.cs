namespace Centra.Workflows;

/// <summary>
/// Abstract base class for typed workflow activities.
/// </summary>
public abstract class WorkflowActivity<TInput, TOutput> : IWorkflowActivity<TInput, TOutput>
{
    public abstract ValueTask<TOutput> RunAsync(WorkflowActivityContext context, TInput input);
}
