namespace Centra.Workflows;

/// <summary>
/// Abstract base class for typed input workflows without output.
/// </summary>
public abstract class Workflow<TInput> : IWorkflow
{
    public abstract ValueTask RunAsync(IWorkflowContext context, TInput input);
}
