using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Defines a contract for invoking workflow orchestrator RunAsync methods with cached dynamic execution delegates.
/// </summary>
public interface IWorkflowRunnerInvoker
{
    /// <summary>
    /// Invokes the workflow instance's RunAsync method and returns the untyped output.
    /// </summary>
    ValueTask<object?> InvokeWorkflowRunAsync(
        object workflowInstance,
        IWorkflowContext context,
        object? typedInput);
}
