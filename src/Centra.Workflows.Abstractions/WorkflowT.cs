namespace Centra.Workflows;

/// <summary>
/// Abstract base class for typed input and output workflows.
/// </summary>
public abstract class Workflow<TInput, TOutput> : IWorkflow
{
    public abstract ValueTask<TOutput> RunAsync(IWorkflowContext context, TInput input);
}
