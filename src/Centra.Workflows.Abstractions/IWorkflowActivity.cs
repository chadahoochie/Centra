namespace Centra.Workflows;

/// <summary>
/// Defines an atomic, idempotent activity in a distributed workflow.
/// </summary>
public interface IWorkflowActivity<in TInput, TOutput>
{
    ValueTask<TOutput> RunAsync(WorkflowActivityContext context, TInput input);
}
