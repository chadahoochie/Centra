using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Defines a contract for invoking activity methods with cached dynamic execution delegates.
/// </summary>
public interface IWorkflowActivityMethodInvoker
{
    /// <summary>
    /// Invokes the RunAsync method on the activity instance and returns the typed output.
    /// </summary>
    ValueTask<TOutput> InvokeActivityMethodAsync<TOutput>(
        object activityInstance,
        WorkflowActivityContext context,
        object? typedInput,
        CancellationToken cancellationToken);
}
